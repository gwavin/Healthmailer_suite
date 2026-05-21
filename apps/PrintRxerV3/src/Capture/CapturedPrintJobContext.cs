namespace PrintRxerV3.Capture;

public sealed record CapturedPrintJobContext
{
    public required string CaptureDirectory { get; init; }
    public required string PayloadPath { get; init; }
    public required string DocumentName { get; init; }
    public required string PrinterName { get; init; }
    public required string PrintJobId { get; init; }
    public string SubmittingUser { get; init; } = string.Empty;
    public DateTimeOffset? CapturedAtUtc { get; init; }
    public string PatientName { get; init; } = string.Empty;
    public string Mrn { get; init; } = string.Empty;
    public string PrescribedBy { get; init; } = string.Empty;
}
