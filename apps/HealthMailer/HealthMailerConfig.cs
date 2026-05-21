using System.Text.Json;
using System.Text.Json.Serialization;

namespace HealthMailer;

public sealed class HealthMailerConfig
{
    public string HandoffRoot { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "printRxer", "handoff");
    public string LocalRoot { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HealthMailer");
    public int PollIntervalSeconds { get; set; } = 5;
    public int StaleLockMinutes { get; set; } = 30;
    public bool WriteHtmlSummary { get; set; }
    public ChartCopyOptions ChartCopy { get; set; } = new();
    public LoggingOptions Logging { get; set; } = new();
    public bool SendMail { get; set; } = true;

    [JsonIgnore]
    public string SentRoot => Path.Combine(LocalRoot, "sent");

    [JsonIgnore]
    public string FailedRoot => Path.Combine(LocalRoot, "failed");

    [JsonIgnore]
    public string QuarantineRoot => Path.Combine(LocalRoot, "quarantine");

    [JsonIgnore]
    public string LogsRoot => Path.Combine(LocalRoot, "logs");

    [JsonIgnore]
    public string LedgerPath => Path.Combine(LocalRoot, "processed-ledger.jsonl");

    [JsonIgnore]
    public string ConfigPath => Path.Combine(LocalRoot, "healthmailer.settings.json");

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(LocalRoot);
        Directory.CreateDirectory(SentRoot);
        Directory.CreateDirectory(FailedRoot);
        Directory.CreateDirectory(QuarantineRoot);
        Directory.CreateDirectory(LogsRoot);
        SecurityUtilities.TryHardenRuntimeDirectory(LocalRoot);
        SecurityUtilities.TryHardenArchiveDirectory(SentRoot);
        SecurityUtilities.TryHardenArchiveDirectory(FailedRoot);
        SecurityUtilities.TryHardenArchiveDirectory(QuarantineRoot);
        SecurityUtilities.TryHardenLogDirectory(LogsRoot);
        SecurityUtilities.TryHardenLedgerFile(LedgerPath);
        if (!IsUncPath(HandoffRoot))
        {
            TryCreateDirectory(HandoffRoot);
            if (Directory.Exists(HandoffRoot))
            {
                SecurityUtilities.TryHardenDropDirectory(HandoffRoot);
            }
        }
        if (ChartCopy.Enabled && !string.IsNullOrWhiteSpace(ChartCopy.DestinationRoot))
        {
            TryCreateDirectory(ChartCopy.DestinationRoot);
        }
    }

    private static void TryCreateDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }
    }

    private static bool IsUncPath(string path)
    {
        return path.StartsWith(@"\\", StringComparison.Ordinal);
    }

    public static string DefaultConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HealthMailer", "healthmailer.settings.json");

    public static HealthMailerConfig Load(string? path = null)
    {
        string configPath = string.IsNullOrWhiteSpace(path) ? DefaultConfigPath : path;
        if (!File.Exists(configPath))
        {
            HealthMailerConfig created = new();
            created.EnsureDirectories();
            created.Save(configPath);
            return created;
        }

        JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
        HealthMailerConfig config = JsonSerializer.Deserialize<HealthMailerConfig>(File.ReadAllText(configPath), options) ?? new HealthMailerConfig();
        if (string.IsNullOrWhiteSpace(config.HandoffRoot))
        {
            config.HandoffRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "printRxer", "handoff");
        }

        if (string.IsNullOrWhiteSpace(config.LocalRoot))
        {
            config.LocalRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HealthMailer");
        }

        if (config.PollIntervalSeconds <= 0)
        {
            config.PollIntervalSeconds = 5;
        }

        if (config.StaleLockMinutes <= 0)
        {
            config.StaleLockMinutes = 30;
        }

        config.ChartCopy ??= new ChartCopyOptions();
        config.Logging ??= new LoggingOptions();
        config.Logging.Normalize();
        config.EnsureDirectories();
        return config;
    }

    public void Save(string? path = null)
    {
        string configPath = string.IsNullOrWhiteSpace(path) ? ConfigPath : path;
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        JsonSerializerOptions options = new() { WriteIndented = true };
        File.WriteAllText(configPath, JsonSerializer.Serialize(this, options));
        SecurityUtilities.TryHardenConfigDirectory(Path.GetDirectoryName(configPath)!);
        SecurityUtilities.TryHardenConfigFile(configPath);
    }
}

public sealed class ChartCopyOptions
{
    public bool Enabled { get; set; }
    public string DestinationRoot { get; set; } = string.Empty;
    public string FileNameTemplate { get; set; } = "Rx-{MRN}-{PackageId}.pdf";
    public bool RequireMrn { get; set; } = true;
}

public sealed class LoggingOptions
{
    public long MaxLogBytes { get; set; } = 10 * 1024 * 1024;
    public int MaxLogFiles { get; set; } = 5;

    public void Normalize()
    {
        if (MaxLogBytes <= 0)
        {
            MaxLogBytes = 10 * 1024 * 1024;
        }

        if (MaxLogFiles < 0)
        {
            MaxLogFiles = 5;
        }
    }
}
