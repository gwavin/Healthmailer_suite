namespace PrintRxerV3.Capture;

public sealed record CapturedPrintJobResult
{
    public required string CaptureDirectory { get; init; }
    public required string ProcessedCaptureDirectory { get; init; }
    public string? PackageDirectory { get; init; }
    public string? LocalPackageDirectory { get; init; }
    public required bool PackageCreated { get; init; }
    public required string Outcome { get; init; }
}
