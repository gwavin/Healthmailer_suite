namespace PrintRxerV3.Capture;

using PrintRxerV3.Metadata;

public sealed record CapturedPrintJobProcessorOptions
{
    public required string IncomingRoot { get; init; }
    public required string ProcessedRoot { get; init; }
    public string? DeferredRoot { get; init; }
    public required string HandoffRoot { get; init; }
    public string? LocalOutboxRoot { get; init; }
    public string? PublishedRoot { get; init; }
    public string? FailedRoot { get; init; }
    public Func<CapturedPrintJobContext, PickerSelection?>? SelectRecipient { get; init; }
    public Func<string, string, string>? PreparePdfFromCapture { get; init; }
    public Func<string>? PackageIdProvider { get; init; }
    public Action? CleanupCompletedPrintJobs { get; init; }
    public bool RequireJobOwnerMatch { get; init; } = true;
    public bool AllowMissingJobOwnerForImport { get; init; }
    public Func<string?>? CurrentUserSidProvider { get; init; }
    public int PayloadStableSeconds { get; init; } = 1;
    public int MetadataGraceSeconds { get; init; } = 60;
    public TimeSpan PayloadStabilityProbeDelay { get; init; } = TimeSpan.FromMilliseconds(100);
    public Action<string>? Log { get; init; }
}
