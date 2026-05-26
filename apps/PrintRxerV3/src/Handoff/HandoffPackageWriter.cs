using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PrintRxerV3.Metadata;

namespace PrintRxerV3.Handoff;

public static class HandoffPackageWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Write(string handoffRoot, PackageRequest request, string pdfPath)
    {
        if (string.IsNullOrWhiteSpace(handoffRoot))
        {
            throw new ArgumentException("Handoff root is required.", nameof(handoffRoot));
        }

        ArgumentNullException.ThrowIfNull(request);
        HandoffPackageValidator.ValidatePreparedPdf(pdfPath, request.PdfSha256);

        Directory.CreateDirectory(handoffRoot);
        string packageDirectory = Path.Combine(handoffRoot, request.PackageId);
        if (Directory.Exists(packageDirectory))
        {
            throw new IOException("A HealthMailer handoff package already exists for package ID " + request.PackageId + ".");
        }

        string stagingDirectory = Path.Combine(handoffRoot, ".writing-" + request.PackageId + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(stagingDirectory);

        string requestPath = Path.Combine(stagingDirectory, "request.json");
        string pdfTargetPath = Path.Combine(stagingDirectory, "prescription.pdf");
        string hashPath = Path.Combine(stagingDirectory, "request.sha256");
        string summaryPath = Path.Combine(stagingDirectory, "summary.txt");
        string readyPath = Path.Combine(stagingDirectory, "READY");

        try
        {
            File.Copy(pdfPath, pdfTargetPath, overwrite: false);
            HandoffPackageValidator.ValidatePreparedPdf(pdfTargetPath, request.PdfSha256);
            PackageRequest readyRequest = request with { ReadyAt = DateTimeOffset.UtcNow };
            File.WriteAllText(requestPath, JsonSerializer.Serialize(readyRequest, JsonOptions), Encoding.UTF8);
            File.WriteAllText(hashPath, request.PdfSha256 + "  prescription.pdf" + Environment.NewLine, Encoding.ASCII);
            File.WriteAllText(summaryPath, BuildSummary(readyRequest), Encoding.UTF8);
            File.WriteAllText(readyPath, string.Empty, Encoding.ASCII);
            Directory.Move(stagingDirectory, packageDirectory);
        }
        catch
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }

            throw;
        }

        return packageDirectory;
    }

    private static string BuildSummary(PackageRequest request)
    {
        StringBuilder builder = new();
        builder.AppendLine("printRxer HealthMailer handoff package");
        builder.AppendLine("Package ID: " + request.PackageId);
        builder.AppendLine("Recipient: " + request.SelectedRecipientName + " <" + request.SelectedRecipientEmail + ">");
        builder.AppendLine("Document kind: " + request.DocumentKind);
        builder.AppendLine("Document name: " + request.DocumentName);
        builder.AppendLine("Internal package PDF: prescription.pdf");
        builder.AppendLine("Outbound attachment filename: " + request.AttachmentDisplayName);
        builder.AppendLine("Subject: " + request.Subject);
        builder.AppendLine("PDF SHA256: " + request.PdfSha256);
        builder.AppendLine("Audit note: " + request.AuditNote);
        return builder.ToString();
    }
}
