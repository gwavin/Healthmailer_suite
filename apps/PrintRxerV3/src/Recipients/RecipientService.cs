using System.Text.Json;

namespace PrintRxerV3.Recipients;

public sealed class RecipientService : IDisposable
{
    private readonly RecipientSourceOptions _options;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly object _snapshotLock = new();
    private CancellationTokenSource? _backgroundCancellation;
    private Task? _backgroundTask;
    private RecipientSnapshot _current = RecipientSnapshot.Empty("Recipients have not been loaded.");
    private DateTimeOffset? _lastCentralWriteTimeUtc;
    private long _lastCentralLengthBytes = -1;

    public RecipientService(RecipientSourceOptions options)
    {
        _options = options;
        _options.Validate();
    }

    public RecipientSnapshot Current
    {
        get
        {
            lock (_snapshotLock)
            {
                return _current;
            }
        }
    }

    public RecipientSnapshot LoadLocalFirst()
    {
        Directory.CreateDirectory(_options.LocalRecipientRoot);

        if (_options.Mode != RecipientSourceMode.HandoffDerivedRequired && TryLoadLocal(_options.CacheRecipientFile, RecipientSourceKind.Cache, out RecipientSnapshot cache))
        {
            SetCurrent(cache);
            WriteStatus(cache, centralAvailable: false, centralValid: false, cache.Warning);
            return cache;
        }

        if (_options.UseBundledFallback && _options.Mode != RecipientSourceMode.HandoffDerivedRequired && TryLoadBundled(out RecipientSnapshot bundled))
        {
            SetCurrent(bundled);
            WriteStatus(bundled, centralAvailable: false, centralValid: false, bundled.Warning);
            return bundled;
        }

        RecipientSnapshot none = RecipientSnapshot.Empty("RecipientNoUsableSource");
        SetCurrent(none);
        WriteStatus(none, centralAvailable: false, centralValid: false, none.Warning);
        return none;
    }

    public RecipientRefreshResult RefreshFromCentral()
    {
        if (_options.Mode == RecipientSourceMode.BundledOnly)
        {
            return RecipientRefreshResult.Failed("Central refresh disabled in BundledOnly mode.", Current);
        }

        if (!_refreshLock.Wait(0))
        {
            return RecipientRefreshResult.Failed("Recipient refresh is already running.", Current);
        }

        try
        {
            FileInfo file = new(_options.CentralRecipientFile);
            if (!file.Exists)
            {
                WriteStatus(Current, centralAvailable: false, centralValid: false, "RecipientCentralUnavailable", inspectCentral: true);
                return RecipientRefreshResult.Failed("RecipientCentralUnavailable: " + _options.CentralRecipientFile, Current);
            }

            if (_lastCentralWriteTimeUtc == file.LastWriteTimeUtc && _lastCentralLengthBytes == file.Length && Current.SourceUsed == RecipientSourceKind.Central)
            {
                WriteStatus(Current, centralAvailable: true, centralValid: true, "RecipientCentralRefreshSkippedUnchanged", inspectCentral: true);
                return RecipientRefreshResult.Succeeded("RecipientCentralRefreshSkippedUnchanged", Current);
            }

            IReadOnlyList<RecipientRecord> recipients = RecipientCsvValidator.LoadValidated(file.FullName);
            RecipientSnapshot snapshot = new(recipients, RecipientSourceKind.Central, file.FullName, string.Empty);
            Directory.CreateDirectory(_options.LocalRecipientRoot);
            File.Copy(file.FullName, _options.CacheRecipientFile, overwrite: true);
            _lastCentralWriteTimeUtc = file.LastWriteTimeUtc;
            _lastCentralLengthBytes = file.Length;
            SetCurrent(snapshot);
            WriteStatus(snapshot, centralAvailable: true, centralValid: true, string.Empty, inspectCentral: true);
            return RecipientRefreshResult.Succeeded("RecipientCentralRefreshSucceeded", snapshot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or SystemException)
        {
            WriteStatus(Current, centralAvailable: false, centralValid: false, "RecipientCentralRefreshFailed: " + ex.Message, inspectCentral: true);
            return RecipientRefreshResult.Failed(ex.Message, Current);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void StartBackgroundRefresh(Action<string>? log = null)
    {
        if (!_options.RefreshOnStartup || _backgroundTask is not null)
        {
            return;
        }

        _backgroundCancellation = new CancellationTokenSource();
        CancellationToken token = _backgroundCancellation.Token;
        _backgroundTask = Task.Run(async () =>
        {
            try
            {
                log?.Invoke("RecipientBackgroundRefreshScheduled");
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(_options.StartupRefreshDelaySeconds, 1, 300)), token).ConfigureAwait(false);
                if (!token.IsCancellationRequested)
                {
                    log?.Invoke(RefreshFromCentral().Message);
                }

                using PeriodicTimer timer = new(TimeSpan.FromHours(Math.Max(1, _options.RefreshIntervalHours)));
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    log?.Invoke(RefreshFromCentral().Message);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    public bool TryBeginRefreshForTest() => _refreshLock.Wait(0);

    public void EndRefreshForTest() => _refreshLock.Release();

    public void Dispose()
    {
        _backgroundCancellation?.Cancel();
        _backgroundCancellation?.Dispose();
        _refreshLock.Dispose();
    }

    private bool TryLoadBundled(out RecipientSnapshot snapshot)
    {
        string bundled = File.Exists(_options.BundledRecipientFile) ? _options.BundledRecipientFile : _options.LegacyLocalRecipientFile;
        return TryLoadLocal(bundled, RecipientSourceKind.BundledFallback, out snapshot);
    }

    private static bool TryLoadLocal(string path, RecipientSourceKind sourceKind, out RecipientSnapshot snapshot)
    {
        try
        {
            if (File.Exists(path))
            {
                IReadOnlyList<RecipientRecord> recipients = RecipientCsvValidator.LoadValidated(path);
                snapshot = new RecipientSnapshot(recipients, sourceKind, path, string.Empty);
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or SystemException)
        {
            snapshot = RecipientSnapshot.Empty(ex.Message);
            return false;
        }

        snapshot = RecipientSnapshot.Empty("Recipient source not found: " + path);
        return false;
    }

    private void SetCurrent(RecipientSnapshot snapshot)
    {
        lock (_snapshotLock)
        {
            _current = snapshot;
        }
    }

    private void WriteStatus(RecipientSnapshot snapshot, bool centralAvailable, bool centralValid, string warning, bool inspectCentral = false)
    {
        try
        {
            Directory.CreateDirectory(_options.LocalRecipientRoot);
            FileInfo? central = inspectCentral && File.Exists(_options.CentralRecipientFile) ? new FileInfo(_options.CentralRecipientFile) : null;
            RecipientSourceStatus status = new()
            {
                LastCheckedUtc = DateTimeOffset.UtcNow,
                SourceUsed = snapshot.SourceUsed,
                CentralPath = _options.CentralRecipientFile,
                CachePath = _options.CacheRecipientFile,
                BundledPath = _options.BundledRecipientFile,
                CentralAvailable = centralAvailable,
                CentralValid = centralValid,
                ActiveRecipientCount = snapshot.Recipients.Count,
                CentralLastWriteTimeUtc = central?.LastWriteTimeUtc,
                CentralLengthBytes = central?.Length ?? 0,
                Warning = warning
            };

            File.WriteAllText(_options.StatusFile, JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SystemException)
        {
        }
    }
}
