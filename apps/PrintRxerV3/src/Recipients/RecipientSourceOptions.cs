namespace PrintRxerV3.Recipients;

public enum RecipientSourceMode
{
    BundledOnly,
    HandoffDerivedWithFallback,
    HandoffDerivedRequired
}

public sealed class RecipientSourceOptions
{
    public RecipientSourceMode Mode { get; init; } = RecipientSourceMode.HandoffDerivedWithFallback;
    public string HandoffRoot { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "printRxer", "handoff");
    public string CentralRelativePath { get; init; } = "recipients";
    public string CentralFileName { get; init; } = "recipients.csv";
    public bool UseBundledFallback { get; init; } = true;
    public bool RefreshOnStartup { get; init; } = true;
    public int StartupRefreshDelaySeconds { get; init; } = 20;
    public int RefreshIntervalHours { get; init; } = 12;
    public int MaxCacheAgeDaysWarning { get; init; } = 30;
    public int MaxCacheAgeDaysBlock { get; init; } = 365;
    public string LocalRecipientRoot { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "printRxer", "data", "recipients");

    public string CentralRecipientFolder => Path.Combine(HandoffRoot, CentralRelativePath);
    public string CentralRecipientFile => Path.Combine(CentralRecipientFolder, CentralFileName);
    public string BundledRecipientFile => Path.Combine(LocalRecipientRoot, "bundled-recipients.csv");
    public string LegacyLocalRecipientFile => Path.Combine(LocalRecipientRoot, "recipients.csv");
    public string CacheRecipientFile => Path.Combine(LocalRecipientRoot, "recipients.cache.csv");
    public string StatusFile => Path.Combine(LocalRecipientRoot, "recipient-source-status.json");

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(HandoffRoot))
        {
            throw new InvalidOperationException("HandoffRoot is required.");
        }

        if (string.IsNullOrWhiteSpace(CentralRelativePath) ||
            CentralRelativePath.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(CentralRelativePath) ||
            CentralRelativePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0 ||
            CentralRelativePath.Contains(Path.DirectorySeparatorChar) ||
            CentralRelativePath.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidOperationException("CentralRelativePath must be a simple relative folder name.");
        }

        if (string.IsNullOrWhiteSpace(CentralFileName) ||
            CentralFileName.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(CentralFileName) ||
            CentralFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException("CentralFileName must be a simple file name.");
        }
    }
}
