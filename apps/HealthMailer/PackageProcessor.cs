namespace HealthMailer;

public sealed class PackageProcessor
{
    private readonly HealthMailerConfig _config;
    private readonly IMailHandoff _mailHandoff;
    private readonly IChartCopyWriter _chartCopyWriter;
    private readonly ProcessingAuditWriter _auditWriter = new();
    private readonly ProcessedPackageLedger _ledger;
    private readonly Action<string> _log;

    public PackageProcessor(HealthMailerConfig config, IMailHandoff mailHandoff, Action<string> log)
        : this(config, mailHandoff, new ChartCopyWriter(), log)
    {
    }

    public PackageProcessor(HealthMailerConfig config, IMailHandoff mailHandoff, IChartCopyWriter chartCopyWriter, Action<string> log)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _mailHandoff = mailHandoff ?? throw new ArgumentNullException(nameof(mailHandoff));
        _chartCopyWriter = chartCopyWriter ?? throw new ArgumentNullException(nameof(chartCopyWriter));
        _log = log ?? (_ => { });
        _ledger = new ProcessedPackageLedger(_config.LedgerPath);
    }

    public int ProcessAvailablePackages()
    {
        _config.EnsureDirectories();
        try
        {
            if (!Directory.Exists(_config.HandoffRoot))
            {
                _log("HealthMailer handoff folder is unavailable: " + _config.HandoffRoot);
                return 0;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            _log("HealthMailer handoff folder check failed: " + ex.Message);
            return 0;
        }

        int processed = 0;
        IEnumerable<string> packageDirectories;
        try
        {
            packageDirectories = Directory.EnumerateDirectories(_config.HandoffRoot).OrderBy(path => Directory.GetCreationTimeUtc(path)).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            _log("HealthMailer handoff folder enumeration failed: " + ex.Message);
            return 0;
        }

        foreach (string packageDirectory in packageDirectories)
        {
            if (TryProcessPackage(packageDirectory))
            {
                processed++;
            }
        }

        return processed;
    }

    public bool TryProcessPackage(string packageDirectory)
    {
        string name = Path.GetFileName(packageDirectory);
        if (name.StartsWith(".", StringComparison.Ordinal) || !File.Exists(Path.Combine(packageDirectory, "READY")))
        {
            return false;
        }

        PackageClaim? claim = TryClaimPackage(packageDirectory);
        if (claim is null)
        {
            return false;
        }

        try
        {
            PackageLoadResult loadResult = HandoffPackageLoader.TryLoad(packageDirectory);
            if (!loadResult.Success || loadResult.Package is null)
            {
                WriteAndArchive(packageDirectory, CreateResult(name, PackageOutcome.ValidationFailed, loadResult.Error), _config.QuarantineRoot, claim);
                _log($"Validation failed for package {name}: {loadResult.Error}");
                return true;
            }

            DeliveryPackage package = loadResult.Package;
            if (_ledger.HasSent(package))
            {
                ProcessingResult duplicate = CreateResult(package, PackageOutcome.Duplicate, "Package ID or completed package hash was already sent.", mailSent: false, chartCopied: false);
                WriteAndArchive(packageDirectory, duplicate, _config.QuarantineRoot, claim);
                _log($"Duplicate package quarantined: {package.PackageId}");
                return true;
            }

            if (_config.SendMail)
            {
                _mailHandoff.Send(package);
            }

            string chartPath = string.Empty;
            bool chartCopied = false;
            try
            {
                chartPath = _chartCopyWriter.CopyToChartFolder(package, _config.ChartCopy);
                chartCopied = !string.IsNullOrWhiteSpace(chartPath);
            }
            catch (Exception ex)
            {
                ProcessingResult chartFailed = CreateResult(package, PackageOutcome.ChartCopyFailed, ex.Message, mailSent: _config.SendMail, chartCopied: false);
                _ledger.Append(chartFailed);
                WriteAndArchive(packageDirectory, chartFailed, _config.FailedRoot, claim);
                _log($"Chart copy failed after mail for package {package.PackageId}: {ex.Message}");
                return true;
            }

            ProcessingResult sent = CreateResult(package, PackageOutcome.Sent, "Package processed.", mailSent: _config.SendMail, chartCopied: chartCopied, chartCopyPath: chartPath);
            _ledger.Append(sent);
            WriteAndArchive(packageDirectory, sent, _config.SentRoot, claim);
            _log($"Processed package {package.PackageId} for {package.RecipientEmail}");
            return true;
        }
        catch (Exception ex)
        {
            PackageLoadResult loadResult = HandoffPackageLoader.TryLoad(packageDirectory);
            ProcessingResult failed = loadResult.Package is null
                ? CreateResult(name, PackageOutcome.Failed, ex.Message)
                : CreateResult(loadResult.Package, PackageOutcome.MailFailed, ex.Message, mailSent: false, chartCopied: false);
            if (loadResult.Package is not null)
            {
                _ledger.Append(failed);
            }

            WriteAndArchive(packageDirectory, failed, _config.FailedRoot, claim);
            _log($"Package failed for {name}: {ex.Message}");
            return true;
        }
        finally
        {
            claim.Dispose();
        }
    }

    private PackageClaim? TryClaimPackage(string packageDirectory)
    {
        string lockPath = Path.Combine(packageDirectory, ".healthmailer.lock");
        try
        {
            bool existed = File.Exists(lockPath);
            FileStream lockStream = new(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (existed)
            {
                DateTimeOffset lockTime = ReadLockTime(lockPath, lockStream);
                if (DateTimeOffset.UtcNow - lockTime < TimeSpan.FromMinutes(_config.StaleLockMinutes))
                {
                    lockStream.Dispose();
                    return null;
                }
            }

            lockStream.SetLength(0);
            lockStream.Position = 0;
            byte[] timestamp = System.Text.Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O") + Environment.NewLine);
            lockStream.Write(timestamp);
            lockStream.Flush(flushToDisk: true);
            return new PackageClaim(lockPath, lockStream);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static DateTimeOffset ReadLockTime(string lockPath, FileStream lockStream)
    {
        lockStream.Position = 0;
        byte[] buffer = new byte[lockStream.Length];
        _ = lockStream.Read(buffer);
        string content = System.Text.Encoding.UTF8.GetString(buffer).Trim();
        if (DateTimeOffset.TryParse(content, out DateTimeOffset parsed))
        {
            lockStream.Position = 0;
            return parsed.ToUniversalTime();
        }

        lockStream.Position = 0;
        return File.GetLastWriteTimeUtc(lockPath);
    }

    private void WriteAndArchive(string packageDirectory, ProcessingResult result, string archiveRoot, PackageClaim claim)
    {
        _auditWriter.WriteTerminalRecords(packageDirectory, result, _config);
        claim.Release();
        MoveToArchive(packageDirectory, archiveRoot);
    }

    private static ProcessingResult CreateResult(string packageName, PackageOutcome outcome, string message)
    {
        return new ProcessingResult
        {
            PackageId = packageName,
            Outcome = outcome,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Message = message
        };
    }

    private static ProcessingResult CreateResult(
        DeliveryPackage package,
        PackageOutcome outcome,
        string message,
        bool mailSent,
        bool chartCopied,
        string chartCopyPath = "")
    {
        return new ProcessingResult
        {
            PackageId = package.PackageId,
            Outcome = outcome,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Message = message,
            RecipientEmail = package.RecipientEmail,
            PdfSha256 = package.PdfSha256,
            CompletedPackageHash = package.CompletedPackageHash,
            MailSent = mailSent,
            ChartCopied = chartCopied,
            ChartCopyPath = chartCopyPath
        };
    }

    private static void MoveToArchive(string sourceDirectory, string archiveRoot)
    {
        Directory.CreateDirectory(archiveRoot);
        string destination = Path.Combine(archiveRoot, Path.GetFileName(sourceDirectory));
        if (Directory.Exists(destination))
        {
            destination += "-" + Guid.NewGuid().ToString("N")[..8];
        }

        if (string.Equals(
            Path.GetPathRoot(Path.GetFullPath(sourceDirectory)),
            Path.GetPathRoot(Path.GetFullPath(destination)),
            StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(sourceDirectory, destination);
            return;
        }

        CopyDirectory(sourceDirectory, destination);
        Directory.Delete(sourceDirectory, recursive: true);
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (string directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
        }

        foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFileName(file), ".healthmailer.lock", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relative = Path.GetRelativePath(sourceDirectory, file);
            string destination = Path.Combine(destinationDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: false);
        }
    }

    private sealed class PackageClaim : IDisposable
    {
        private readonly string _lockPath;
        private FileStream? _stream;

        public PackageClaim(string lockPath, FileStream stream)
        {
            _lockPath = lockPath;
            _stream = stream;
        }

        public void Release()
        {
            FileStream? stream = Interlocked.Exchange(ref _stream, null);
            stream?.Dispose();
            try
            {
                if (File.Exists(_lockPath))
                {
                    File.Delete(_lockPath);
                }
            }
            catch (IOException)
            {
            }
        }

        public void Dispose()
        {
            _stream?.Dispose();
        }
    }
}
