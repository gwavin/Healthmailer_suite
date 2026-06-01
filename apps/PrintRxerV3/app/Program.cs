using System.Runtime.Versioning;
using System.Text.Json;
using System.Windows.Forms;
using PrintRxerV3.Capture;
using PrintRxerV3.Metadata;
using PrintRxerV3.Notifications;
using PrintRxerV3.Recipients;

namespace PrintRxerV3.App;

public static class Program
{
    private static PrintRxerV3Log? _log;

    [STAThread]
    [SupportedOSPlatform("windows")]
    public static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (args.Any(arg => arg.Equals("--install", StringComparison.OrdinalIgnoreCase)))
        {
            return RunInstallWizard(args);
        }

        if (args.Any(arg => arg.Equals("--status", StringComparison.OrdinalIgnoreCase)))
        {
            return WriteStatus(args);
        }

        if (args.Any(arg => arg.Equals("--process-once", StringComparison.OrdinalIgnoreCase)))
        {
            return ProcessOnce(args);
        }

        if (args.Any(arg => arg.Equals("--watch", StringComparison.OrdinalIgnoreCase)))
        {
            return Watch(args);
        }

        string outputRoot = GetOption(args, "--output", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "printRxer", "handoff"));
        string packageDirectory = PreviewPackageCreator.CreateSamplePackage(outputRoot);
        WindowsInformationAlert.Show(UserNotificationMessageBuilder.BuildPackageReadyMessage(packageDirectory));
        Console.WriteLine("Created printRxer preview handoff package:");
        Console.WriteLine(packageDirectory);
        return 0;
    }

    private static int ProcessOnce(string[] args)
    {
        CapturedPrintJobProcessor processor = CreateProcessor(args);
        CapturedPrintJobResult? result = processor.ProcessOne();
        if (result is null)
        {
            Console.WriteLine("No ready PrintRxer capture was found, or the user cancelled recipient selection.");
            return 0;
        }

        if (result.PackageCreated && !string.IsNullOrWhiteSpace(result.PackageDirectory))
        {
            WindowsInformationAlert.Show(UserNotificationMessageBuilder.BuildPackageReadyMessage(result.PackageDirectory));
        }
        else if (result.Outcome.Equals("PackagePublishDeferred", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(result.LocalPackageDirectory))
        {
            WindowsInformationAlert.Show(UserNotificationMessageBuilder.BuildPackageQueuedMessage(result.LocalPackageDirectory));
        }
        Console.WriteLine("Created printRxer handoff package from captured print:");
        Console.WriteLine(result.PackageDirectory ?? "(no package created)");
        Console.WriteLine("Local package:");
        Console.WriteLine(result.LocalPackageDirectory ?? "(no local package)");
        Console.WriteLine("Outcome:");
        Console.WriteLine(result.Outcome);
        Console.WriteLine("Moved capture to:");
        Console.WriteLine(result.ProcessedCaptureDirectory);
        return 0;
    }

    private static int Watch(string[] args)
    {
        PrintRxerV3Config config = LoadConfig(args);
        using CancellationTokenSource cancellation = new();
        Console.CancelKeyPress += delegate(object? sender, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        Console.WriteLine("Watching PrintRxer captures. Press Ctrl+C to stop.");
        Console.WriteLine("Incoming: " + config.IncomingRoot);
        Console.WriteLine("Handoff:  " + config.HandoffRoot);
        Console.WriteLine("Temp:     " + config.TempRoot);
        Log("printRxer started. Incoming: " + config.IncomingRoot + "; Handoff: " + config.HandoffRoot);

        CapturedPrintJobWatcher watcher = new(new CapturedPrintJobWatcherOptions
        {
            Processor = CreateProcessor(args),
            NotifyPackageReady = packageDirectory =>
            {
                WindowsInformationAlert.Show(UserNotificationMessageBuilder.BuildPackageReadyMessage(packageDirectory));
                Log("Created printRxer handoff package: " + packageDirectory);
                Console.WriteLine("Created printRxer handoff package: " + packageDirectory);
            },
            NotifyPackageQueuedLocal = localPackageDirectory =>
            {
                WindowsInformationAlert.Show(UserNotificationMessageBuilder.BuildPackageQueuedMessage(localPackageDirectory));
                Log("Package queued locally; handoff folder unavailable; will retry automatically. Local package: " + localPackageDirectory);
                Console.WriteLine("Package queued locally; handoff folder unavailable; will retry automatically. Local package: " + localPackageDirectory);
            },
            PollInterval = TimeSpan.FromSeconds(config.RetryIntervalSeconds)
        });

        watcher.RunUntilCancelled(cancellation.Token);
        Console.WriteLine("Stopped printRxer watcher.");
        return 0;
    }

    private static CapturedPrintJobProcessor CreateProcessor(string[] args)
    {
        PrintRxerV3Config config = LoadConfig(args);
        return new CapturedPrintJobProcessor(new CapturedPrintJobProcessorOptions
        {
            IncomingRoot = config.IncomingRoot,
            ProcessedRoot = config.ProcessedRoot,
            DeferredRoot = config.DeferredRoot,
            LocalOutboxRoot = config.LocalOutboxRoot,
            PublishedRoot = config.PublishedRoot,
            FailedRoot = config.FailedRoot,
            HandoffRoot = config.HandoffRoot,
            RequireJobOwnerMatch = !args.Any(arg => arg.Equals("--no-job-owner-match", StringComparison.OrdinalIgnoreCase)),
            AllowMissingJobOwnerForImport = args.Any(arg => arg.Equals("--allow-missing-job-owner", StringComparison.OrdinalIgnoreCase)),
            PayloadStableSeconds = GetIntOption(args, "--payload-stable-seconds", config.PayloadStableSeconds),
            MetadataGraceSeconds = GetIntOption(args, "--metadata-grace-seconds", config.MetadataGraceSeconds),
            Log = Log,
            CleanupCompletedPrintJobs = PrintQueueCleaner.RemoveCompletedPrintRxerJobs,
            SelectRecipient = args.Any(arg => arg.Equals("--no-picker", StringComparison.OrdinalIgnoreCase))
                ? null
                : context => SelectRecipient(context, config)
        });
    }

    private static int WriteStatus(string[] args)
    {
        bool json = args.Any(arg => arg.Equals("--json", StringComparison.OrdinalIgnoreCase));
        string? configPath = GetOptionalOption(args, "--config");
        PrintRxerV3Config config = PrintRxerV3Config.Load(configPath);
        string effectiveConfigPath = string.IsNullOrWhiteSpace(configPath) ? PrintRxerV3Config.DefaultConfigPath : configPath;
        List<string> warnings = [];
        bool handoffReachable = Directory.Exists(config.HandoffRoot);
        if (!handoffReachable)
        {
            warnings.Add("Handoff folder is unavailable.");
        }

        DirectoryInfo[] pending = EnumerateDirectories(config.LocalOutboxRoot);
        DateTimeOffset? oldestPending = pending.Length == 0 ? null : pending.Min(directory => directory.CreationTimeUtc);
        if (oldestPending is not null && DateTimeOffset.UtcNow - oldestPending.Value > TimeSpan.FromMinutes(10))
        {
            warnings.Add("Pending outbox contains packages older than 10 minutes.");
        }

        string logPath = Path.Combine(config.LogsRoot, "printRxer.log");
        long diskFree = GetDiskFreeBytes(config.LocalOutboxRoot);
        if (diskFree >= 0 && diskFree < 1024L * 1024L * 1024L)
        {
            warnings.Add("Free disk space is below 1 GB.");
        }

        var status = new
        {
            Component = "printRxer",
            ConfigPath = effectiveConfigPath,
            config.IncomingRoot,
            config.HandoffRoot,
            HandoffReachable = handoffReachable,
            config.LocalOutboxRoot,
            PendingOutboxCount = pending.Length,
            OldestPendingPackageAgeMinutes = oldestPending is null ? (double?)null : Math.Round((DateTimeOffset.UtcNow - oldestPending.Value).TotalMinutes, 1),
            ProcessedCount = EnumerateDirectories(config.ProcessedRoot).Length,
            DeferredCount = EnumerateDirectories(config.DeferredRoot).Length,
            FailedCount = EnumerateDirectories(config.FailedRoot).Length,
            PublishedCount = EnumerateDirectories(config.PublishedRoot).Length,
            LogPath = logPath,
            ActiveLogSizeBytes = File.Exists(logPath) ? new FileInfo(logPath).Length : 0,
            DiskFreeBytes = diskFree,
            Warnings = warnings
        };

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine("printRxer status");
            Console.WriteLine("Config: " + status.ConfigPath);
            Console.WriteLine("Incoming: " + status.IncomingRoot);
            Console.WriteLine("Handoff: " + status.HandoffRoot + " (" + (handoffReachable ? "reachable" : "unavailable") + ")");
            Console.WriteLine("Pending outbox: " + status.PendingOutboxCount);
            Console.WriteLine("Oldest pending age minutes: " + (status.OldestPendingPackageAgeMinutes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"));
            Console.WriteLine("Published/failed/deferred/processed: " + status.PublishedCount + "/" + status.FailedCount + "/" + status.DeferredCount + "/" + status.ProcessedCount);
            foreach (string warning in warnings)
            {
                Console.WriteLine("WARNING: " + warning);
            }
        }

        return handoffReachable ? (warnings.Count == 0 ? 0 : 1) : 2;
    }

    private static DirectoryInfo[] EnumerateDirectories(string root)
    {
        try
        {
            return Directory.Exists(root) ? new DirectoryInfo(root).EnumerateDirectories().ToArray() : [];
        }
        catch
        {
            return [];
        }
    }

    private static long GetDiskFreeBytes(string path)
    {
        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(path)) ?? path;
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return -1;
        }
    }

    private static PrintRxerV3Config LoadConfig(string[] args)
    {
        PrintRxerV3Config config = PrintRxerV3Config.Load(GetOptionalOption(args, "--config"));
        config.IncomingRoot = GetOption(args, "--incoming", config.IncomingRoot);
        config.ProcessedRoot = GetOption(args, "--processed", config.ProcessedRoot);
        config.HandoffRoot = GetOption(args, "--output", config.HandoffRoot);
        config.Normalize();
        config.EnsureLocalDirectories();
        _log = new PrintRxerV3Log(config.LogsRoot, config.MaxLogBytes, config.MaxLogFiles);
        return config;
    }

    private static int RunInstallWizard(string[] args)
    {
        PrintRxerV3Config config = PrintRxerV3Config.Load(GetOptionalOption(args, "--config"));
        string? incomingOverride = GetOptionalOption(args, "--incoming");
        string? dataRootOverride = GetOptionalOption(args, "--data-root");
        string? handoffOverride = GetOptionalOption(args, "--output");
        if (!string.IsNullOrWhiteSpace(incomingOverride) || !string.IsNullOrWhiteSpace(dataRootOverride) || !string.IsNullOrWhiteSpace(handoffOverride))
        {
            string selectedDataRoot = string.IsNullOrWhiteSpace(dataRootOverride)
                ? Path.GetDirectoryName(config.ProcessedRoot) ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "printRxer")
                : dataRootOverride;
            config.IncomingRoot = string.IsNullOrWhiteSpace(incomingOverride) ? config.IncomingRoot : incomingOverride;
            ApplyDataRoot(config, selectedDataRoot);
            config.HandoffRoot = string.IsNullOrWhiteSpace(handoffOverride) ? config.HandoffRoot : handoffOverride;
            config.Normalize();
            config.EnsureLocalDirectories();
            config.Save();
            InstallScheduledTask(config.ConfigPath);
            Console.WriteLine("printRxer installed.");
            Console.WriteLine("Config: " + config.ConfigPath);
            Console.WriteLine("Handoff: " + config.HandoffRoot);
            return 0;
        }

        using FolderBrowserDialog incomingDialog = new()
        {
            Description = "Select the incoming print capture folder.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = config.IncomingRoot
        };
        if (incomingDialog.ShowDialog() != DialogResult.OK)
        {
            return 2;
        }

        using FolderBrowserDialog dataDialog = new()
        {
            Description = "Select the local printRxer data folder.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = Path.GetDirectoryName(config.ProcessedRoot) ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "printRxer")
        };
        if (dataDialog.ShowDialog() != DialogResult.OK)
        {
            return 2;
        }

        using FolderBrowserDialog handoffDialog = new()
        {
            Description = "Select the HealthMailer handoff folder. This may be a UNC shared folder.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = config.HandoffRoot
        };
        if (handoffDialog.ShowDialog() != DialogResult.OK)
        {
            return 2;
        }

        string dataRoot = dataDialog.SelectedPath;
        config.IncomingRoot = incomingDialog.SelectedPath;
        ApplyDataRoot(config, dataRoot);
        config.HandoffRoot = handoffDialog.SelectedPath;
        config.Normalize();
        config.EnsureLocalDirectories();
        config.Save();
        InstallScheduledTask(config.ConfigPath);

        MessageBox.Show(
            "printRxer is configured and will start at user logon." + Environment.NewLine + Environment.NewLine +
            "Config: " + config.ConfigPath,
            "printRxer installed",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        return 0;
    }

    private static void ApplyDataRoot(PrintRxerV3Config config, string dataRoot)
    {
        config.ProcessedRoot = Path.Combine(dataRoot, "processed");
        config.DeferredRoot = Path.Combine(dataRoot, "deferred");
        config.LocalOutboxRoot = Path.Combine(dataRoot, "pending-outbox");
        config.PublishedRoot = Path.Combine(dataRoot, "published");
        config.FailedRoot = Path.Combine(dataRoot, "failed");
        config.LogsRoot = Path.Combine(dataRoot, "logs");
        config.TempRoot = Path.Combine(dataRoot, "temp");
    }

    private static void InstallScheduledTask(string configPath)
    {
        string exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? throw new InvalidOperationException("Could not resolve printRxer executable path.");
        string command =
            "$action = New-ScheduledTaskAction -Execute '" + EscapePowerShellSingleQuoted(exePath) + "' -Argument '--watch --config \"" + EscapePowerShellSingleQuoted(configPath) + "\"'; " +
            "$logonTrigger = New-ScheduledTaskTrigger -AtLogOn; " +
            "$principal = New-ScheduledTaskPrincipal -GroupId 'BUILTIN\\Users' -RunLevel Limited; " +
            "$settings = New-ScheduledTaskSettingsSet -MultipleInstances Parallel -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit (New-TimeSpan -Days 999) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable; " +
            "Register-ScheduledTask -TaskName 'printRxer' -Action $action -Trigger $logonTrigger -Principal $principal -Settings $settings -Force | Out-Null";
        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" + command.Replace("\"", "\\\"") + "\"",
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not start powershell.exe.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("Scheduled task registration failed with exit code " + process.ExitCode + ".");
        }
    }

    private static string EscapePowerShellSingleQuoted(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    [SupportedOSPlatform("windows")]
    private static PickerSelection? SelectRecipient(CapturedPrintJobContext context, PrintRxerV3Config config)
    {
        using ProgressNotice notice = new("Preparing print job details. The recipient picker will open shortly.");
        notice.Show();
        notice.Refresh();
        RecipientService recipientService = RecipientSource.GetService(config);
        RecipientSnapshot snapshot = recipientService.Current;
        if (!snapshot.HasRecipients)
        {
            snapshot = recipientService.LoadLocalFirst();
        }
        notice.Close();

        if (!snapshot.HasRecipients)
        {
            Log("RecipientNoUsableSource: " + snapshot.Warning);
            MessageBox.Show(
                "No usable recipient list is available. The document has not been sent or prepared. Please contact support to restore the central, cached, or bundled recipient list.",
                "Recipient list unavailable",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return null;
        }

        using RecipientSelectionDialog dialog = new(
            snapshot.Recipients,
            context,
            () => PreviewPrescription(context, config),
            FormatRecipientSource(snapshot),
            recipientService.RefreshFromCentral);
        DialogResult result = dialog.ShowDialog();
        if (result != DialogResult.OK)
        {
            return null;
        }

        return dialog.Selection;
    }

    private static string FormatRecipientSource(RecipientSnapshot snapshot)
    {
        return snapshot.SourceUsed switch
        {
            RecipientSourceKind.Central => "central list",
            RecipientSourceKind.Cache => "cached central list from " + File.GetLastWriteTime(snapshot.SourcePath).ToString("dd MMM yyyy HH:mm"),
            RecipientSourceKind.BundledFallback => "bundled fallback list",
            _ => "no usable recipient list"
        };
    }

    private static void PreviewPrescription(CapturedPrintJobContext context, PrintRxerV3Config config)
    {
        try
        {
            Log("PreviewPrescriptionRequested: rendering temporary preview.");
            string previewPath = PreviewPrescriptionService.PreparePreviewPdf(context, config.TempRoot);
            PreviewPrescriptionService.OpenWithDefaultViewer(previewPath);
            Log("PreviewPrescriptionOpened: " + previewPath);
        }
        catch (Exception ex)
        {
            Log("PreviewPrescriptionFailed: " + ex.GetType().Name + ": " + ex.Message);
            throw;
        }
    }

    private static string GetOption(string[] args, string name, string fallback)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return fallback;
    }

    private static string? GetOptionalOption(string[] args, string name)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static int GetIntOption(string[] args, string name, int fallback)
    {
        string value = GetOption(args, name, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int parsed) && parsed >= 0
            ? parsed
            : fallback;
    }

    internal static void Log(string message)
    {
        try
        {
            (_log ??= new PrintRxerV3Log(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "printRxer", "logs"),
                5 * 1024 * 1024,
                3)).Write(message);
        }
        catch
        {
            try
            {
                Console.Error.WriteLine(message);
            }
            catch
            {
            }
        }
    }
}
