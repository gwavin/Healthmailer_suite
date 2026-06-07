using System.Text.Json;
using HealthMailer;

namespace HealthMailer.Tests;

public sealed class HandoffPackageTests
{
    [Test]
    public void TryLoadReadyPackage_rejects_directory_without_ready_marker()
    {
        string packageDirectory = CreatePackage(includeReady: false);

        PackageLoadResult result = HandoffPackageLoader.TryLoad(packageDirectory);

        Assert.False(result.Success);
        Assert.Contains("READY", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public void TryLoadReadyPackage_rejects_hash_mismatch()
    {
        string packageDirectory = CreatePackage();
        File.WriteAllText(Path.Combine(packageDirectory, "request.sha256"), "bad  prescription.pdf");

        PackageLoadResult result = HandoffPackageLoader.TryLoad(packageDirectory);

        Assert.False(result.Success);
        Assert.Contains("SHA256", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public void TryLoadReadyPackage_maps_v3_request_to_delivery_package()
    {
        string packageDirectory = CreatePackage();

        PackageLoadResult result = HandoffPackageLoader.TryLoad(packageDirectory);

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.Package);
        Assert.Equal("alpha@example.ie", result.Package.RecipientEmail);
        Assert.Equal("Prescription for review", result.Package.Subject);
        Assert.Equal("Jane Doe", result.Package.PatientName);
        Assert.Equal("MRN123", result.Package.Mrn);
        Assert.Equal("Prescription", result.Package.DocumentKind);
        Assert.Equal("Prescription", result.Package.DocumentName);
        Assert.Equal("MRN123_prescription_20260526_1430.pdf", result.Package.AttachmentDisplayName);
        Assert.EndsWith("prescription.pdf", result.Package.AttachmentPath);
    }

    [Test]
    public void TryLoadReadyPackage_completed_hash_is_deterministic()
    {
        string packageDirectory = CreatePackage();

        PackageLoadResult first = HandoffPackageLoader.TryLoad(packageDirectory);
        PackageLoadResult second = HandoffPackageLoader.TryLoad(packageDirectory);

        Assert.True(first.Success, first.Error);
        Assert.True(second.Success, second.Error);
        Assert.Equal(first.Package!.CompletedPackageHash, second.Package!.CompletedPackageHash);
    }

    [Test]
    public void TryLoadReadyPackage_retries_transient_payload_file_lock()
    {
        string packageDirectory = CreatePackage();
        string requestPath = Path.Combine(packageDirectory, "request.json");
        using FileStream transientLock = File.Open(requestPath, FileMode.Open, FileAccess.Read, FileShare.None);
        Task releaseLock = Task.Run(() =>
        {
            Thread.Sleep(300);
            transientLock.Dispose();
        });

        PackageLoadResult result = HandoffPackageLoader.TryLoad(packageDirectory);
        releaseLock.GetAwaiter().GetResult();

        Assert.True(result.Success, result.Error);
    }

    private static string CreatePackage(bool includeReady = true)
    {
        string packageDirectory = Path.Combine(Path.GetTempPath(), "healthmailer-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(packageDirectory);
        string pdfPath = Path.Combine(packageDirectory, "prescription.pdf");
        File.WriteAllText(pdfPath, "%PDF-1.4\n% test\n");
        string hash = SecurityUtilities.ComputeSha256(pdfPath);
        object request = new
        {
            packageId = "pkg-123",
            selectedRecipientEmail = "alpha@example.ie",
            selectedRecipientName = "Alpha Pharmacy",
            subject = "Prescription for review",
            body = "Please see attached.",
            documentKind = "Prescription",
            documentName = "Prescription",
            attachmentDisplayName = "MRN123_prescription_20260526_1430.pdf",
            pdfSha256 = hash,
            patientName = "Jane Doe",
            mrn = "MRN123",
            pickerSelection = new
            {
                recipientName = "Alpha Pharmacy",
                recipientEmail = "alpha@example.ie",
                subject = "Prescription for review",
                body = "Please see attached.",
                documentKind = "Prescription",
                documentName = "Prescription",
                attachmentDisplayName = "MRN123_prescription_20260526_1430.pdf"
            },
            printJobOrigin = new
            {
                documentName = "Rx Jane Doe MRN123"
            }
        };
        File.WriteAllText(Path.Combine(packageDirectory, "request.json"), JsonSerializer.Serialize(request));
        File.WriteAllText(Path.Combine(packageDirectory, "request.sha256"), hash + "  prescription.pdf" + Environment.NewLine);
        if (includeReady)
        {
            File.WriteAllText(Path.Combine(packageDirectory, "READY"), string.Empty);
        }

        return packageDirectory;
    }
}
