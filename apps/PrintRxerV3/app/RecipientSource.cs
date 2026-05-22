using PrintRxerV3.Recipients;
using PrintRxerV3.Capture;

namespace PrintRxerV3.App;

public static class RecipientSource
{
    private static readonly object ServiceLock = new();
    private static RecipientService? _service;

    public static RecipientService GetService(PrintRxerV3Config config)
    {
        lock (ServiceLock)
        {
            _service ??= CreateService(config);
            return _service;
        }
    }

    public static IReadOnlyList<RecipientRecord> LoadDefault()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "printRxer", "data", "recipients");
        if (Directory.Exists(folder))
        {
            string canonicalCsv = Path.Combine(folder, "recipients.csv");
            string? file = File.Exists(canonicalCsv)
                ? canonicalCsv
                : Directory.EnumerateFiles(folder, "*.*")
                    .Where(path => IsSupported(path))
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
            if (file is not null)
            {
                return RecipientCsvLoader.LoadAny(file);
            }
        }

        return new[]
        {
            new RecipientRecord
            {
                RecipientName = "Sample HealthMailer Recipient",
                EmailAddress = "sample.recipient@example.invalid",
                Aliases = Array.Empty<string>(),
                SearchTerms = new[] { "Sample HealthMailer Recipient", "sample.recipient@example.invalid" },
                SearchText = "sample healthmailer recipient sample.recipient@example.invalid"
            }
        };
    }

    private static RecipientService CreateService(PrintRxerV3Config config)
    {
        RecipientSourceSettings settings = config.RecipientSource;
        RecipientSourceMode mode = Enum.TryParse(settings.Mode, ignoreCase: true, out RecipientSourceMode parsed)
            ? parsed
            : RecipientSourceMode.HandoffDerivedWithFallback;

        RecipientSourceOptions options = new()
        {
            Mode = mode,
            HandoffRoot = config.HandoffRoot,
            CentralRelativePath = settings.CentralRelativePath,
            CentralFileName = settings.CentralFileName,
            UseBundledFallback = settings.UseBundledFallback,
            RefreshOnStartup = settings.RefreshOnStartup,
            StartupRefreshDelaySeconds = settings.StartupRefreshDelaySeconds,
            RefreshIntervalHours = settings.RefreshIntervalHours,
            MaxCacheAgeDaysWarning = settings.MaxCacheAgeDaysWarning,
            MaxCacheAgeDaysBlock = settings.MaxCacheAgeDaysBlock
        };

        RecipientService service = new(options);
        Program.Log("RecipientStartupLocalLoadStarted");
        RecipientSnapshot snapshot = service.LoadLocalFirst();
        Program.Log(snapshot.HasRecipients ? "RecipientStartupLocalLoadSucceeded: " + snapshot.SourceUsed : "RecipientStartupLocalLoadFailed: " + snapshot.Warning);
        service.StartBackgroundRefresh(Program.Log);
        return service;
    }

    private static bool IsSupported(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase);
    }
}
