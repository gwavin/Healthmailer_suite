using System.Diagnostics;
using System.Text.Json;
using PrintRxerV3.Capture;
using Xunit;

namespace PrintRxerV3.Tests;

public sealed class PrintRxerV3ConfigTests
{
    [Fact]
    public void EnsureLocalDirectories_repeated_calls_do_not_leak_process_handles()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-handles-" + Guid.NewGuid().ToString("N"));
        PrintRxerV3Config config = new()
        {
            IncomingRoot = Path.Combine(root, "incoming"),
            ProcessedRoot = Path.Combine(root, "processed"),
            DeferredRoot = Path.Combine(root, "deferred"),
            LocalOutboxRoot = Path.Combine(root, "pending-outbox"),
            PublishedRoot = Path.Combine(root, "published"),
            FailedRoot = Path.Combine(root, "failed"),
            LogsRoot = Path.Combine(root, "logs"),
            TempRoot = Path.Combine(root, "temp"),
            HandoffRoot = Path.Combine(root, "handoff")
        };

        config.EnsureLocalDirectories();
        int before = Process.GetCurrentProcess().HandleCount;

        for (int index = 0; index < 20; index++)
        {
            config.EnsureLocalDirectories();
        }

        int after = Process.GetCurrentProcess().HandleCount;
        Assert.True(after - before <= 5, $"Handle count grew by {after - before}.");
    }

    [Fact]
    public void Load_roundtrips_persistent_config_fields()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-config-" + Guid.NewGuid().ToString("N"));
        string configPath = Path.Combine(root, "config", "printrxer_v3.settings.json");
        PrintRxerV3Config config = new()
        {
            IncomingRoot = Path.Combine(root, "incoming"),
            ProcessedRoot = Path.Combine(root, "processed"),
            DeferredRoot = Path.Combine(root, "deferred"),
            LocalOutboxRoot = Path.Combine(root, "pending-outbox"),
            PublishedRoot = Path.Combine(root, "published"),
            FailedRoot = Path.Combine(root, "failed"),
            LogsRoot = Path.Combine(root, "logs"),
            HandoffRoot = @"\\server\HealthMailerDrop$\incoming",
            PayloadStableSeconds = 7,
            RequireJobOwnerMatch = true,
            AllowMissingSubmittingSid = false,
            RetryIntervalSeconds = 42,
            MaxLogBytes = 2048,
            MaxLogFiles = 4
        };

        config.Save(configPath);
        PrintRxerV3Config loaded = PrintRxerV3Config.Load(configPath);

        Assert.Equal(config.HandoffRoot, loaded.HandoffRoot);
        Assert.Equal(config.LocalOutboxRoot, loaded.LocalOutboxRoot);
        Assert.Equal(42, loaded.RetryIntervalSeconds);
        Assert.Equal(2048, loaded.MaxLogBytes);
        Assert.True(Directory.Exists(loaded.LogsRoot));
    }

    [Fact]
    public void Defaults_are_tuned_for_fast_picker_startup_with_stability_check()
    {
        PrintRxerV3Config config = new()
        {
            PayloadStableSeconds = 0,
            RetryIntervalSeconds = 0
        };

        config.Normalize();

        Assert.Equal(1, config.PayloadStableSeconds);
        Assert.Equal(1, config.RetryIntervalSeconds);
    }

    [Fact]
    public void Temp_root_defaults_under_local_data_area()
    {
        PrintRxerV3Config config = new();

        Assert.EndsWith(Path.Combine("printRxer", "temp"), config.TempRoot, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Default_paths_use_final_printRxer_product_name_for_new_installs()
    {
        PrintRxerV3Config config = new();

        Assert.EndsWith(Path.Combine("printRxer", "work", "incoming"), config.IncomingRoot, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("printRxer", "handoff"), config.HandoffRoot, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("printRxer", "config", "printRxer.settings.json"), PrintRxerV3Config.DefaultConfigPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrintRxerV3Log_rotates_and_caps_retained_files()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-log-" + Guid.NewGuid().ToString("N"));
        PrintRxerV3Log log = new(root, maxLogBytes: 120, maxLogFiles: 2);

        for (int index = 0; index < 20; index++)
        {
            log.Write("rotation test line " + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Assert.True(File.Exists(Path.Combine(root, "printRxer.log")));
        Assert.True(File.Exists(Path.Combine(root, "printRxer.1.log")));
        Assert.True(File.Exists(Path.Combine(root, "printRxer.2.log")));
        Assert.False(File.Exists(Path.Combine(root, "printRxer.3.log")));
    }

    [Fact]
    public void Install_script_writes_config_without_registering_task()
    {
        string repoRoot = FindRepoRoot();
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-install-script-" + Guid.NewGuid().ToString("N"));
        string fakeExe = Path.Combine(root, "printRxer.exe");
        string configPath = Path.Combine(root, "config", "printRxer.settings.json");
        Directory.CreateDirectory(root);
        File.WriteAllText(fakeExe, "fake exe");

        ProcessStartInfo start = new()
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(repoRoot, "tools", "Install-printRxerTask.ps1") + "\" " +
                "-ExePath \"" + fakeExe + "\" " +
                "-IncomingRoot \"" + Path.Combine(root, "incoming") + "\" " +
                "-DataRoot \"" + Path.Combine(root, "data") + "\" " +
                "-HandoffRoot \"\\\\server\\HealthMailerDrop$\\incoming\" " +
                "-ConfigPath \"" + configPath + "\" " +
                "-RetryIntervalSeconds 45 " +
                "-SkipTaskRegistration",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell.");
        process.WaitForExit(30000);
        string stderr = process.StandardError.ReadToEnd();

        Assert.Equal(0, process.ExitCode);
        Assert.True(File.Exists(configPath), stderr);
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(configPath));
        Assert.Equal(@"\\server\HealthMailerDrop$\incoming", json.RootElement.GetProperty("HandoffRoot").GetString());
        Assert.Equal(45, json.RootElement.GetProperty("RetryIntervalSeconds").GetInt32());
        Assert.True(json.RootElement.TryGetProperty("LogsRoot", out _));
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "tools")) && File.Exists(Path.Combine(directory.FullName, "PrintRxerSuite.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repo root.");
    }
}
