namespace PrintRxerV3.Metadata;

public sealed record PrintJobOrigin
{
    public string? Source { get; init; }
    public string? PrinterName { get; init; }
    public string? DocumentName { get; init; }
    public string? PrintJobId { get; init; }
    public DateTimeOffset? CapturedAtUtc { get; init; }
    public string? SubmittingUser { get; init; }
    public string? SubmittingUserSid { get; init; }
    public int? SubmittingSessionId { get; init; }
    public string? PatientName { get; init; }
    public string? Mrn { get; init; }
    public string? PrescribedBy { get; init; }
}
