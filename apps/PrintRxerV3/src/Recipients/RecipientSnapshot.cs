using System.Text.Json.Serialization;

namespace PrintRxerV3.Recipients;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RecipientSourceKind
{
    Central,
    Cache,
    BundledFallback,
    None
}

public sealed record RecipientSnapshot(IReadOnlyList<RecipientRecord> Recipients, RecipientSourceKind SourceUsed, string SourcePath, string Warning)
{
    public bool HasRecipients => Recipients.Count > 0;

    public static RecipientSnapshot Empty(string warning) => new(Array.Empty<RecipientRecord>(), RecipientSourceKind.None, string.Empty, warning);
}

public sealed record RecipientRefreshResult(bool Success, string Message, RecipientSnapshot Snapshot)
{
    public static RecipientRefreshResult Failed(string message, RecipientSnapshot snapshot) => new(false, message, snapshot);
    public static RecipientRefreshResult Succeeded(string message, RecipientSnapshot snapshot) => new(true, message, snapshot);
}

public sealed class RecipientSourceStatus
{
    public DateTimeOffset LastCheckedUtc { get; init; }
    public RecipientSourceKind SourceUsed { get; init; }
    public string CentralPath { get; init; } = string.Empty;
    public string CachePath { get; init; } = string.Empty;
    public string BundledPath { get; init; } = string.Empty;
    public bool CentralAvailable { get; init; }
    public bool CentralValid { get; init; }
    public int ActiveRecipientCount { get; init; }
    public DateTimeOffset? CentralLastWriteTimeUtc { get; init; }
    public long CentralLengthBytes { get; init; }
    public string Warning { get; init; } = string.Empty;
}
