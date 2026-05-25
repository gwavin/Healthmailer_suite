using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Windows.Forms;

namespace HealthMailer;

public static class Program
{
    private static HealthMailerLog? _log;

    [STAThread]
    [SupportedOSPlatform("windows")]
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Any(arg => arg.Equals("--install", StringComparison.OrdinalIgnoreCase)))
            {
                return RunInstallWizard();
            }

            if (args.Any(arg => arg.Equals("--validate", StringComparison.OrdinalIgnoreCase)))
            {
                HealthMailerConfig config = HealthMailerConfig.Load(GetOption(args, "--config"));
                ValidateConfiguration(config);
                Console.WriteLine("HealthMailer validation completed.");
                return 0;
            }

            if (args.Any(arg => arg.Equals("--status", StringComparison.OrdinalIgnoreCase)))
            {
                return WriteStatus(args);
            }

            HealthMailerConfig loaded = HealthMailerConfig.Load(GetOption(args, "--config"));
            _log = new HealthMailerLog(loaded.LogsRoot, loaded.Logging);
            if (args.Any(arg => arg.Equals("--process-once", StringComparison.OrdinalIgnoreCase)))
            {
                PackageProcessor processor = CreateProcessor(loaded);
                int processed = processor.ProcessAvailablePackages();
                Console.WriteLine("Processed packages: " + processed);
                return 0;
            }

            return await RunWatcherAsync(loaded).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogFallback("Fatal error: " + ex);
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static async Task<int> RunWatcherAsync(HealthMailerConfig config)
    {
        PackageProcessor processor = CreateProcessor(config);
        using HandoffWatcher watcher = new(config, processor, Log);
        using CancellationTokenSource cancellation = new();
        Console.CancelKeyPress += delegate(object? sender, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        Log("HealthMailer started.");
        Log("Watching handoff directory: " + config.HandoffRoot);
        await watcher.RunAsync(cancellation.Token).ConfigureAwait(false);
        return 0;
    }

    private static PackageProcessor CreateProcessor(HealthMailerConfig config)
    {
        _log = new HealthMailerLog(config.LogsRoot, config.Logging);
        IMailHandoff mailer = config.SendMail ? new OutlookMailHandoff() : new NoopMailHandoff();
        return new PackageProcessor(config, mailer, Log);
    }

    private static int WriteStatus(string[] args)
    {
        bool json = args.Any(arg => arg.Equals("--json", StringComparison.OrdinalIgnoreCase));
        string? configPath = GetOption(args, "--config");
        HealthMailerConfig config = HealthMailerConfig.Load(configPath);
        string effectiveConfigPath = string.IsNullOrWhiteSpace(configPath) ? HealthMailerConfig.DefaultConfigPath : configPath;
        List<string> warnings = [];
        bool handoffReachable = Directory.Exists(config.HandoffRoot);
        if (!handoffReachable)
        {
            warnings.Add("Handoff folder is unavailable.");
        }

        DirectoryInfo[] readyPackages = EnumerateReadyPackages(config.HandoffRoot);
        DateTimeOffset? oldestReady = readyPackages.Length == 0 ? null : readyPackages.Min(directory => directory.CreationTimeUtc);
        if (oldestReady is not null && DateTimeOffset.UtcNow - oldestReady.Value > TimeSpan.FromMinutes(10))
        {
            warnings.Add("Handoff folder contains READY packages older than 10 minutes.");
        }

        int failedCount = EnumerateDirectories(config.FailedRoot).Length;
        int quarantineCount = EnumerateDirectories(config.QuarantineRoot).Length;
        if (failedCount > 0)
        {
            warnings.Add("Failed packages are present.");
        }

        if (quarantineCount > 0)
        {
            warnings.Add("Quarantined packages are present.");
        }

        string outlookStatus = config.SendMail ? "unknown" : "skipped";
        if (config.SendMail)
        {
            try
            {
                OutlookMailHandoff.ValidateOutlookRegistration();
                outlookStatus = "valid";
            }
            catch (Exception ex)
            {
                outlookStatus = "invalid: " + ex.Message;
                warnings.Add("Outlook registration validation failed.");
            }
        }

        string logPath = Path.Combine(config.LogsRoot, "healthmailer.log");
        long diskFree = GetDiskFreeBytes(config.LocalRoot);
        if (diskFree >= 0 && diskFree < 1024L * 1024L * 1024L)
        {
            warnings.Add("Free disk space is below 1 GB.");
        }

        var status = new
        {
            Component = "HealthMailer",
            ConfigPath = effectiveConfigPath,
            config.HandoffRoot,
            HandoffReachable = handoffReachable,
            ReadyPackageCount = readyPackages.Length,
            OldestReadyPackageAgeMinutes = oldestReady is null ? (double?)null : Math.Round((DateTimeOffset.UtcNow - oldestReady.Value).TotalMinutes, 1),
            SentCount = EnumerateDirectories(config.SentRoot).Length,
            FailedCount = failedCount,
            QuarantineCount = quarantineCount,
            ProcessedLedgerPath = config.LedgerPath,
            OutlookRegistration = outlookStatus,
            SendMailEnabled = config.SendMail,
            ChartCopyEnabled = config.ChartCopy.Enabled,
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
            Console.WriteLine("HealthMailer status");
            Console.WriteLine("Config: " + status.ConfigPath);
            Console.WriteLine("Handoff: " + status.HandoffRoot + " (" + (handoffReachable ? "reachable" : "unavailable") + ")");
            Console.WriteLine("READY packages: " + status.ReadyPackageCount);
            Console.WriteLine("Oldest READY age minutes: " + (status.OldestReadyPackageAgeMinutes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"));
            Console.WriteLine("Sent/failed/quarantine: " + status.SentCount + "/" + failedCount + "/" + quarantineCount);
            Console.WriteLine("Outlook: " + outlookStatus);
            foreach (string warning in warnings)
            {
                Console.WriteLine("WARNING: " + warning);
            }
        }

        return handoffReachable && !outlookStatus.StartsWith("invalid", StringComparison.OrdinalIgnoreCase)
            ? (warnings.Count == 0 ? 0 : 1)
            : 2;
    }

    private static DirectoryInfo[] EnumerateReadyPackages(string root)
    {
        try
        {
            return Directory.Exists(root)
                ? new DirectoryInfo(root).EnumerateDirectories()
                    .Where(directory => !directory.Name.StartsWith(".", StringComparison.Ordinal) && File.Exists(Path.Combine(directory.FullName, "READY")))
                    .ToArray()
                : [];
        }
        catch
        {
            return [];
        }
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

    [SupportedOSPlatform("windows")]
    private static int RunInstallWizard()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using FolderBrowserDialog handoffDialog = new()
        {
            Description = "Select the printRxer handoff folder. This may be a secured shared folder.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "printRxer", "handoff")
        };
        if (handoffDialog.ShowDialog() != DialogResult.OK)
        {
            Console.WriteLine("Install cancelled.");
            return 2;
        }

        using FolderBrowserDialog chartDialog = new()
        {
            Description = "Optional: select the ViewPoint/chart import folder. Press Cancel to skip chart copy for now.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        ChartCopyOptions chartOptions = new();
        if (chartDialog.ShowDialog() == DialogResult.OK)
        {
            chartOptions.Enabled = true;
            chartOptions.DestinationRoot = chartDialog.SelectedPath;
        }

        HealthMailerConfig config = new()
        {
            HandoffRoot = handoffDialog.SelectedPath,
            SendMail = false,
            ConfigCreatedByInstaller = true,
            LiveSendingApproved = false,
            ChartCopy = chartOptions
        };
        config.EnsureDirectories();
        config.Save();
        InstallScheduledTask(config.ConfigPath);
        ValidateConfiguration(config);

        MessageBox.Show(
            "HealthMailer is configured and will start at user logon." + Environment.NewLine + Environment.NewLine +
            "Config: " + config.ConfigPath,
            "HealthMailer installed",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        return 0;
    }

    private static void InstallScheduledTask(string configPath)
    {
        string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? throw new InvalidOperationException("Could not resolve HealthMailer executable path.");
        string command =
            "$action = New-ScheduledTaskAction -Execute '" + EscapePowerShellSingleQuoted(exePath) + "' -Argument '--watch --config \"" + EscapePowerShellSingleQuoted(configPath) + "\"'; " +
            "$logonTrigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME; " +
            "$watchdogTrigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 1) -RepetitionDuration (New-TimeSpan -Days 999); " +
            "$principal = New-ScheduledTaskPrincipal -UserId \"$env:USERDOMAIN\\$env:USERNAME\" -LogonType Interactive -RunLevel Limited; " +
            "$settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit (New-TimeSpan -Days 999) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable; " +
            "Register-ScheduledTask -TaskName 'HealthMailer' -Action $action -Trigger @($logonTrigger, $watchdogTrigger) -Principal $principal -Settings $settings -Force | Out-Null; " +
            "Start-ScheduledTask -TaskName 'HealthMailer'";
        using Process process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" + command.Replace("\"", "\\\"") + "\"",
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not start schtasks.exe.");
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

    public static void ValidateConfiguration(HealthMailerConfig config)
    {
        ValidateConfiguration(config, OutlookMailHandoff.ValidateOutlookRegistration);
    }

    public static void ValidateConfiguration(HealthMailerConfig config, Func<string> validateOutlook)
    {
        config.EnsureDirectories();
        config.Logging.Normalize();
        if (config.SendMail)
        {
            if (!config.ConfigCreatedByInstaller || !config.LiveSendingApproved)
            {
                throw new InvalidOperationException("HealthMailer live sending is not approved by installer-created configuration. Run HealthMailerSetup.exe or the quiet installer to create an approved live-sending configuration.");
            }

            validateOutlook();
        }

        if (config.ChartCopy.Enabled && string.IsNullOrWhiteSpace(config.ChartCopy.DestinationRoot))
        {
            throw new InvalidOperationException("Chart copy is enabled but no destination root is configured.");
        }
    }

    private static string? GetOption(string[] args, string name)
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

    private static void Log(string message)
    {
        try
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HealthMailer", "logs");
            (_log ??= new HealthMailerLog(root)).Write(message);
        }
        catch
        {
            LogFallback(message);
        }
    }

    private static void LogFallback(string message)
    {
        try
        {
            Console.Error.WriteLine(message);
        }
        catch
        {
        }
    }

    private sealed class NoopMailHandoff : IMailHandoff
    {
        public void Send(DeliveryPackage package)
        {
        }
    }
}
