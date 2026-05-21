using System.Text.Json;

namespace HealthMailer;

public interface IChartCopyWriter
{
    string CopyToChartFolder(DeliveryPackage package, ChartCopyOptions options);
}

public sealed class ChartCopyWriter : IChartCopyWriter
{
    public string CopyToChartFolder(DeliveryPackage package, ChartCopyOptions options)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(options.DestinationRoot))
        {
            throw new InvalidOperationException("Chart copy is enabled but no destination root is configured.");
        }

        if (options.RequireMrn && string.IsNullOrWhiteSpace(package.Mrn))
        {
            throw new InvalidOperationException("Chart copy requires MRN metadata, but this package does not contain an MRN.");
        }

        Directory.CreateDirectory(options.DestinationRoot);
        string fileName = BuildFileName(package, options.FileNameTemplate);
        string destinationPath = Path.Combine(options.DestinationRoot, fileName);
        if (File.Exists(destinationPath))
        {
            string stem = Path.GetFileNameWithoutExtension(fileName);
            destinationPath = Path.Combine(options.DestinationRoot, stem + "-" + Guid.NewGuid().ToString("N")[..8] + ".pdf");
        }

        File.Copy(package.AttachmentPath, destinationPath, overwrite: false);
        File.WriteAllText(Path.ChangeExtension(destinationPath, ".json"), JsonSerializer.Serialize(new
        {
            package.PackageId,
            package.PatientName,
            package.Mrn,
            package.PdfSha256,
            CopiedAt = DateTimeOffset.UtcNow
        }, new JsonSerializerOptions { WriteIndented = true }));
        return destinationPath;
    }

    private static string BuildFileName(DeliveryPackage package, string template)
    {
        string result = string.IsNullOrWhiteSpace(template) ? "Rx-{MRN}-{PackageId}.pdf" : template;
        result = result.Replace("{MRN}", SecurityUtilities.SanitizeFileComponent(package.Mrn), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{PatientName}", SecurityUtilities.SanitizeFileComponent(package.PatientName), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{PackageId}", SecurityUtilities.SanitizeFileComponent(package.PackageId), StringComparison.OrdinalIgnoreCase);
        if (!result.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            result += ".pdf";
        }

        return SecurityUtilities.SanitizeFileComponent(result);
    }
}
