using System.Security.Cryptography;
using System.Text.Json;
using PrintRxerV3.Handoff;
using PrintRxerV3.Metadata;
using PrintRxerV3.Packaging;
using Xunit;

namespace PrintRxerV3.Tests;

public sealed class PackagingTests
{
    [Fact]
    public void PackageIdGenerator_creates_sortable_identifier_with_random_suffix()
    {
        DateTimeOffset timestamp = new(2026, 5, 9, 14, 30, 15, 123, TimeSpan.Zero);

        string packageId = PackageIdGenerator.Create(timestamp);

        Assert.Matches(@"^20260509-143015123-[0-9a-f]{12}$", packageId);
    }

    [Fact]
    public void Sha256Hasher_returns_lowercase_hex_hash_for_file()
    {
        string filePath = Path.Combine(Path.GetTempPath(), "printrxer-v3-hash-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(filePath, "abc");

        string hash = Sha256Hasher.HashFile(filePath);

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
    }

    [Fact]
    public void RequestBuilder_includes_required_workstation_picker_and_audit_metadata()
    {
        DateTimeOffset timestamp = new(2026, 5, 9, 14, 35, 0, TimeSpan.Zero);
        PackageRequest request = PackageRequestBuilder.Create(
            packageId: "pkg-1",
            pdfSha256: "hash",
            createdAt: timestamp,
            identity: new WorkstationIdentity
            {
                WindowsUser = "GAVIN",
                DomainUser = "DOMAIN\\GAVIN",
                UserSid = "S-1-5-21",
                SessionId = 7,
                WorkstationName = "WS01",
                WorkstationDomain = "DOMAIN"
            },
            printOrigin: new PrintJobOrigin
            {
                Source = "port-monitor",
                PrinterName = "printRxer",
                DocumentName = "Prescription",
                PrintJobId = "42",
                CapturedAtUtc = timestamp
            },
            selection: new PickerSelection
            {
                RecipientName = "Alpha Pharmacy",
                RecipientEmail = "alpha@example.ie",
                Subject = "Prescription",
                Body = "Please process.",
                SelectedAt = timestamp
            });

        Assert.Equal("pkg-1", request.PackageId);
        Assert.Equal("hash", request.PdfSha256);
        Assert.Equal("Alpha Pharmacy", request.PickerSelection.RecipientName);
        Assert.Equal("alpha@example.ie", request.SelectedRecipientEmail);
        Assert.Equal("Alpha Pharmacy", request.SelectedRecipient.Name);
        Assert.Equal("alpha@example.ie", request.SelectedRecipient.Email);
        Assert.Equal("RecipientSelected", request.PickerOutcome);
        Assert.Equal("printRxer created this HealthMailer handoff package as workstation audit evidence; it did not send mail.", request.AuditNote);
    }

    [Fact]
    public void HandoffPackageWriter_writes_expected_files_and_ready_marker_last()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-package-" + Guid.NewGuid().ToString("N"));
        string pdfPath = Path.Combine(root, "source.pdf");
        Directory.CreateDirectory(root);
        File.WriteAllText(pdfPath, "%PDF-1.4\n% test pdf\n");
        DateTimeOffset timestamp = new(2026, 5, 9, 15, 0, 0, TimeSpan.Zero);
        PackageRequest request = PackageRequestBuilder.Create(
            "pkg-2",
            Sha256Hasher.HashFile(pdfPath),
            timestamp,
            new WorkstationIdentity { WindowsUser = "GAVIN", DomainUser = "DOMAIN\\GAVIN", UserSid = "S-1", SessionId = 1, WorkstationName = "WS01", WorkstationDomain = "DOMAIN" },
            new PrintJobOrigin { Source = "sample" },
            new PickerSelection { RecipientName = "Beta", RecipientEmail = "beta@example.ie", Subject = "Subject", Body = "Body", SelectedAt = timestamp });

        HandoffPackageWriter.Write(root, request, pdfPath);

        string packageDirectory = Path.Combine(root, "pkg-2");
        Assert.True(File.Exists(Path.Combine(packageDirectory, "request.json")));
        Assert.True(File.Exists(Path.Combine(packageDirectory, "prescription.pdf")));
        Assert.True(File.Exists(Path.Combine(packageDirectory, "request.sha256")));
        Assert.True(File.Exists(Path.Combine(packageDirectory, "summary.txt")));
        Assert.True(File.Exists(Path.Combine(packageDirectory, "READY")));
        Assert.True(File.GetLastWriteTimeUtc(Path.Combine(packageDirectory, "READY")) >= File.GetLastWriteTimeUtc(Path.Combine(packageDirectory, "request.json")));

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(packageDirectory, "request.json")));
        Assert.Equal("pkg-2", document.RootElement.GetProperty("packageId").GetString());
        Assert.Equal("beta@example.ie", document.RootElement.GetProperty("selectedRecipientEmail").GetString());
    }

    [Fact]
    public void HandoffPackageWriter_rejects_non_pdf_payload_before_ready_marker()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-package-invalid-" + Guid.NewGuid().ToString("N"));
        string pdfPath = Path.Combine(root, "source.pdf");
        Directory.CreateDirectory(root);
        File.WriteAllText(pdfPath, "not a pdf");
        DateTimeOffset timestamp = new(2026, 5, 9, 15, 0, 0, TimeSpan.Zero);
        PackageRequest request = PackageRequestBuilder.Create(
            "pkg-invalid",
            Sha256Hasher.HashFile(pdfPath),
            timestamp,
            new WorkstationIdentity { WindowsUser = "GAVIN", DomainUser = "DOMAIN\\GAVIN", UserSid = "S-1", SessionId = 1, WorkstationName = "WS01", WorkstationDomain = "DOMAIN" },
            new PrintJobOrigin { Source = "sample" },
            new PickerSelection { RecipientName = "Beta", RecipientEmail = "beta@example.ie", Subject = "Subject", Body = "Body", SelectedAt = timestamp });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => HandoffPackageWriter.Write(root, request, pdfPath));

        Assert.Contains("not a PDF", exception.Message);
        Assert.False(Directory.Exists(Path.Combine(root, "pkg-invalid")));
        Assert.Empty(Directory.EnumerateDirectories(root).Where(path => Path.GetFileName(path).StartsWith(".writing-", StringComparison.Ordinal)));
    }

    [Fact]
    public void HandoffPackageWriter_rejects_hash_mismatch()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-package-hash-" + Guid.NewGuid().ToString("N"));
        string pdfPath = Path.Combine(root, "source.pdf");
        Directory.CreateDirectory(root);
        File.WriteAllText(pdfPath, "%PDF-1.4\n% test pdf\n");
        DateTimeOffset timestamp = new(2026, 5, 9, 15, 0, 0, TimeSpan.Zero);
        PackageRequest request = PackageRequestBuilder.Create(
            "pkg-hash",
            "bad-hash",
            timestamp,
            new WorkstationIdentity { WindowsUser = "GAVIN", DomainUser = "DOMAIN\\GAVIN", UserSid = "S-1", SessionId = 1, WorkstationName = "WS01", WorkstationDomain = "DOMAIN" },
            new PrintJobOrigin { Source = "sample" },
            new PickerSelection { RecipientName = "Beta", RecipientEmail = "beta@example.ie", Subject = "Subject", Body = "Body", SelectedAt = timestamp });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => HandoffPackageWriter.Write(root, request, pdfPath));

        Assert.Contains("SHA256", exception.Message);
        Assert.False(Directory.Exists(Path.Combine(root, "pkg-hash")));
    }

    [Fact]
    public void HandoffPackagePublisher_treats_existing_complete_matching_final_package_as_idempotent()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-publisher-idempotent-" + Guid.NewGuid().ToString("N"));
        string localOutbox = Path.Combine(root, "pending-outbox");
        string handoff = Path.Combine(root, "handoff");
        string published = Path.Combine(root, "published");
        string localPackage = CreatePackage(localOutbox, "pkg-existing");
        CopyDirectory(localPackage, Path.Combine(handoff, "pkg-existing"));

        HandoffPackagePublisher publisher = new();

        HandoffPublishResult result = publisher.TryPublish(localPackage, handoff, published);

        Assert.True(result.Published);
        Assert.Equal("PackagePublished", result.Outcome);
        Assert.False(Directory.Exists(localPackage));
        Assert.True(Directory.Exists(Path.Combine(published, "pkg-existing")));
    }

    [Fact]
    public void HandoffPackagePublisher_leaves_local_package_queued_when_existing_final_package_is_incomplete()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-publisher-incomplete-" + Guid.NewGuid().ToString("N"));
        string localOutbox = Path.Combine(root, "pending-outbox");
        string handoff = Path.Combine(root, "handoff");
        string published = Path.Combine(root, "published");
        string localPackage = CreatePackage(localOutbox, "pkg-existing");
        string finalPackage = Path.Combine(handoff, "pkg-existing");
        CopyDirectory(localPackage, finalPackage);
        File.Delete(Path.Combine(finalPackage, "request.sha256"));

        HandoffPackagePublisher publisher = new();

        HandoffPublishResult result = publisher.TryPublish(localPackage, handoff, published);

        Assert.False(result.Published);
        Assert.Equal("PackagePublishDeferred", result.Outcome);
        Assert.True(Directory.Exists(localPackage));
        Assert.False(Directory.Exists(Path.Combine(published, "pkg-existing")));
    }

    [Fact]
    public void HandoffPackagePublisher_leaves_local_package_queued_when_existing_final_package_is_mismatched()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-publisher-mismatch-" + Guid.NewGuid().ToString("N"));
        string localOutbox = Path.Combine(root, "pending-outbox");
        string handoff = Path.Combine(root, "handoff");
        string published = Path.Combine(root, "published");
        string localPackage = CreatePackage(localOutbox, "pkg-existing");
        string finalPackage = Path.Combine(handoff, "pkg-existing");
        CopyDirectory(localPackage, finalPackage);
        File.WriteAllText(Path.Combine(finalPackage, "summary.txt"), "different package with same ID");

        HandoffPackagePublisher publisher = new();

        HandoffPublishResult result = publisher.TryPublish(localPackage, handoff, published);

        Assert.False(result.Published);
        Assert.Equal("PackagePublishFailed", result.Outcome);
        Assert.True(Directory.Exists(localPackage));
        Assert.False(Directory.Exists(Path.Combine(published, "pkg-existing")));
    }

    private static string CreatePackage(string root, string packageId)
    {
        Directory.CreateDirectory(root);
        string pdfPath = Path.Combine(root, packageId + ".pdf");
        File.WriteAllText(pdfPath, "%PDF-1.4\n% test pdf\n");
        DateTimeOffset timestamp = new(2026, 5, 9, 15, 0, 0, TimeSpan.Zero);
        PackageRequest request = PackageRequestBuilder.Create(
            packageId,
            Sha256Hasher.HashFile(pdfPath),
            timestamp,
            new WorkstationIdentity { WindowsUser = "GAVIN", DomainUser = "DOMAIN\\GAVIN", UserSid = "S-1", SessionId = 1, WorkstationName = "WS01", WorkstationDomain = "DOMAIN" },
            new PrintJobOrigin { Source = "sample" },
            new PickerSelection { RecipientName = "Beta", RecipientEmail = "beta@example.ie", Subject = "Subject", Body = "Body", SelectedAt = timestamp });

        return HandoffPackageWriter.Write(root, request, pdfPath);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
    }
}
