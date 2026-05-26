namespace HealthMailer;

public sealed record DeliveryPackage
{
    public required string PackageDirectory { get; init; }
    public required string PackageId { get; init; }
    public required string RecipientEmail { get; init; }
    public required string RecipientName { get; init; }
    public required string Subject { get; init; }
    public required string Body { get; init; }
    public required string AttachmentPath { get; init; }
    public required string PdfSha256 { get; init; }
    public required string CompletedPackageHash { get; init; }
    public string DocumentKind { get; init; } = "ClinicalDocument";
    public string DocumentName { get; init; } = "Clinical document";
    public string AttachmentDisplayName { get; init; } = "clinicalDocument.pdf";
    public string PatientName { get; init; } = string.Empty;
    public string Mrn { get; init; } = string.Empty;
}

public sealed record PackageLoadResult
{
    public bool Success { get; init; }
    public string Error { get; init; } = string.Empty;
    public DeliveryPackage? Package { get; init; }

    public static PackageLoadResult Fail(string error) => new() { Success = false, Error = error };
    public static PackageLoadResult Ok(DeliveryPackage package) => new() { Success = true, Package = package };
}
