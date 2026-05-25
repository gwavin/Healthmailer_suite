using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;

namespace HealthMailer;

public static class HandoffPackageLoader
{
    public static PackageLoadResult TryLoad(string packageDirectory)
    {
        try
        {
            string directoryName = Path.GetFileName(packageDirectory);
            if (directoryName.StartsWith(".", StringComparison.Ordinal))
            {
                return PackageLoadResult.Fail("Staging directories are ignored.");
            }

            string readyPath = Path.Combine(packageDirectory, "READY");
            if (!File.Exists(readyPath))
            {
                return PackageLoadResult.Fail("Package does not contain READY marker.");
            }

            string requestPath = Path.Combine(packageDirectory, "request.json");
            string pdfPath = Path.Combine(packageDirectory, "prescription.pdf");
            string hashPath = Path.Combine(packageDirectory, "request.sha256");
            if (!File.Exists(requestPath) || !File.Exists(pdfPath) || !File.Exists(hashPath))
            {
                return PackageLoadResult.Fail("Package is missing request.json, prescription.pdf, or request.sha256.");
            }

            if (!SecurityUtilities.LooksLikePdf(pdfPath))
            {
                return PackageLoadResult.Fail("prescription.pdf is not a PDF.");
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(requestPath));
            JsonElement root = document.RootElement;
            string expectedHash = Read(root, "pdfSha256");
            string actualHash = SecurityUtilities.ComputeSha256(pdfPath);
            if (string.IsNullOrWhiteSpace(expectedHash) || !actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                return PackageLoadResult.Fail("PDF SHA256 does not match request.json.");
            }

            string shaFile = File.ReadAllText(hashPath).Trim();
            if (!shaFile.StartsWith(actualHash, StringComparison.OrdinalIgnoreCase) || !shaFile.EndsWith("prescription.pdf", StringComparison.OrdinalIgnoreCase))
            {
                return PackageLoadResult.Fail("PDF SHA256 does not match request.sha256.");
            }

            string recipientEmail = FirstNonEmpty(
                Read(root, "selectedRecipientEmail"),
                ReadNested(root, "selectedRecipient", "email"),
                ReadNested(root, "pickerSelection", "recipientEmail"));
            string recipientName = FirstNonEmpty(
                Read(root, "selectedRecipientName"),
                ReadNested(root, "selectedRecipient", "name"),
                ReadNested(root, "pickerSelection", "recipientName"));
            string subject = FirstNonEmpty(Read(root, "subject"), ReadNested(root, "pickerSelection", "subject"), "Clinical document");
            string body = FirstNonEmpty(Read(root, "body"), ReadNested(root, "pickerSelection", "body"));
            string packageId = FirstNonEmpty(Read(root, "packageId"), directoryName);
            string patientName = FirstNonEmpty(Read(root, "patientName"), ReadNested(root, "patient", "name"));
            string mrn = FirstNonEmpty(Read(root, "mrn"), Read(root, "MRN"), ReadNested(root, "patient", "mrn"));

            string originText = ReadNested(root, "printJobOrigin", "documentName") + " " + body + " " + subject;
            if (string.IsNullOrWhiteSpace(mrn))
            {
                mrn = TryExtractMrn(originText);
            }

            if (string.IsNullOrWhiteSpace(patientName))
            {
                patientName = TryExtractPatientName(originText);
            }

            return PackageLoadResult.Ok(new DeliveryPackage
            {
                PackageDirectory = packageDirectory,
                PackageId = packageId,
                RecipientEmail = recipientEmail,
                RecipientName = recipientName,
                Subject = subject,
                Body = body,
                AttachmentPath = pdfPath,
                PdfSha256 = actualHash,
                CompletedPackageHash = ComputeCompletedPackageHash(requestPath, pdfPath, hashPath),
                PatientName = patientName,
                Mrn = mrn
            });
        }
        catch (Exception ex)
        {
            return PackageLoadResult.Fail(ex.Message);
        }
    }

    private static string Read(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement value) ? value.ToString() : string.Empty;
    }

    private static string ReadNested(JsonElement root, string parent, string child)
    {
        return root.TryGetProperty(parent, out JsonElement parentElement) && parentElement.ValueKind == JsonValueKind.Object
            ? Read(parentElement, child)
            : string.Empty;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static string TryExtractMrn(string value)
    {
        Match match = Regex.Match(value ?? string.Empty, @"\b(?:MRN|MR|Chart\s*No\.?)\s*[:#-]?\s*([A-Za-z0-9-]{3,32})\b", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string TryExtractPatientName(string value)
    {
        Match match = Regex.Match(value ?? string.Empty, @"\bPatient\s*[:#-]?\s*([A-Za-z][A-Za-z '\-]{1,80})", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    private static string ComputeCompletedPackageHash(string requestPath, string pdfPath, string hashPath)
    {
        string combined = SecurityUtilities.ComputeSha256(requestPath) + SecurityUtilities.ComputeSha256(pdfPath) + SecurityUtilities.ComputeSha256(hashPath);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(combined))).ToLowerInvariant();
    }
}
