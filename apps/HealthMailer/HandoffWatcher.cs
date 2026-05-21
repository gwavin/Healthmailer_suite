namespace HealthMailer;

public sealed class HandoffWatcher : IDisposable
{
    private readonly HealthMailerConfig _config;
    private readonly PackageProcessor _processor;
    private readonly Action<string> _log;
    private readonly object _watcherSync = new();
    private FileSystemWatcher? _watcher;
    private bool _watcherUnavailableLogged;
    private int _processing;

    public HandoffWatcher(HealthMailerConfig config, PackageProcessor processor, Action<string> log)
    {
        _config = config;
        _processor = processor;
        _log = log;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _config.EnsureDirectories();
        StartFileSystemWatcher();
        TriggerProcessing();

        while (!cancellationToken.IsCancellationRequested)
        {
            StartFileSystemWatcher();
            TriggerProcessing();
            await Task.Delay(TimeSpan.FromSeconds(_config.PollIntervalSeconds), cancellationToken).ConfigureAwait(false);
        }
    }

    private void StartFileSystemWatcher()
    {
        lock (_watcherSync)
        {
            if (_watcher is not null)
            {
                return;
            }
        }

        if (!Directory.Exists(_config.HandoffRoot))
        {
            LogWatcherStartupFailureOnce("File watcher not started because handoff folder is unavailable: " + _config.HandoffRoot);
            return;
        }

        FileSystemWatcher watcher;
        try
        {
            watcher = new FileSystemWatcher(_config.HandoffRoot)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            LogWatcherStartupFailureOnce("File watcher could not start for handoff folder: " + ex.Message);
            return;
        }

        watcher.Created += (_, _) => TriggerProcessing();
        watcher.Renamed += (_, _) => TriggerProcessing();
        watcher.Changed += (_, _) => TriggerProcessing();
        watcher.Error += (_, args) =>
        {
            _log("File watcher error: " + args.GetException());
            ResetFileSystemWatcher();
        };

        lock (_watcherSync)
        {
            if (_watcher is not null)
            {
                watcher.Dispose();
                return;
            }

            _watcher = watcher;
            _watcherUnavailableLogged = false;
        }

        _log("File watcher started for handoff folder: " + _config.HandoffRoot);
    }

    private void LogWatcherStartupFailureOnce(string message)
    {
        lock (_watcherSync)
        {
            if (_watcherUnavailableLogged)
            {
                return;
            }

            _watcherUnavailableLogged = true;
        }

        _log(message);
    }

    private void ResetFileSystemWatcher()
    {
        FileSystemWatcher? watcher;
        lock (_watcherSync)
        {
            watcher = _watcher;
            _watcher = null;
        }

        watcher?.Dispose();
    }

    private void TriggerProcessing()
    {
        if (Interlocked.Exchange(ref _processing, 1) == 1)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                _processor.ProcessAvailablePackages();
            }
            finally
            {
                Interlocked.Exchange(ref _processing, 0);
            }
        });
    }

    public void Dispose()
    {
        ResetFileSystemWatcher();
    }
}
