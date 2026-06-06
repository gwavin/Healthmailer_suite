using System.Text.Json;

namespace HealthMailer.Tests;

public sealed class ProcessingAuditTests
{
    [Test]
    public void HasSent_detects_sent_package_by_package_id()
    {
        string ledgerPath = CreateLedgerPath();
        ProcessedPackageLedger ledger = new(ledgerPath);
        DeliveryPackage package = CreatePackage("pkg-1", "hash-1");
        ledger.Append(CreateSentResult("pkg-1", "different-hash"));

        Assert.True(ledger.HasSent(package));
    }

    [Test]
    public void HasSent_detects_sent_package_by_completed_hash()
    {
        string ledgerPath = CreateLedgerPath();
        ProcessedPackageLedger ledger = new(ledgerPath);
        DeliveryPackage package = CreatePackage("pkg-2", "hash-2");
        ledger.Append(CreateSentResult("different-package", "hash-2"));

        Assert.True(ledger.HasSent(package));
    }

    [Test]
    public void Append_updates_in_memory_cache()
    {
        string ledgerPath = CreateLedgerPath();
        ProcessedPackageLedger ledger = new(ledgerPath);
        DeliveryPackage package = CreatePackage("pkg-cache", "hash-cache");

        Assert.False(ledger.HasSent(package));
        ledger.Append(CreateSentResult("pkg-cache", "hash-cache"));

        Assert.True(ledger.HasSent(package));
    }

    [Test]
    public void HasSent_reloads_cache_when_ledger_changes_externally()
    {
        string ledgerPath = CreateLedgerPath();
        ProcessedPackageLedger ledger = new(ledgerPath);
        DeliveryPackage package = CreatePackage("pkg-external", "hash-external");

        Assert.False(ledger.HasSent(package));
        Directory.CreateDirectory(Path.GetDirectoryName(ledgerPath)!);
        File.AppendAllText(ledgerPath, JsonSerializer.Serialize(new
        {
            PackageId = "pkg-external",
            outcome = "Sent",
            CompletedPackageHash = "hash-external"
        }) + Environment.NewLine);

        Assert.True(ledger.HasSent(package));
    }

    [Test]
    public void HasSent_treats_mail_sent_true_as_duplicate_protection_even_when_outcome_is_not_sent()
    {
        string ledgerPath = CreateLedgerPath();
        ProcessedPackageLedger ledger = new(ledgerPath);
        DeliveryPackage package = CreatePackage("pkg-chart-failed", "hash-chart-failed");

        ledger.Append(new ProcessingResult
        {
            PackageId = "pkg-chart-failed",
            Outcome = PackageOutcome.ChartCopyFailed,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            RecipientEmail = "recipient@healthmail.ie",
            PdfSha256 = "pdf-hash",
            CompletedPackageHash = "hash-chart-failed",
            MailSent = true
        });

        Assert.True(ledger.HasSent(package));
        Assert.Contains("MailSent", File.ReadAllText(ledgerPath));
    }

    [Test]
    public void HasSent_ignores_malformed_json_lines()
    {
        string ledgerPath = CreateLedgerPath();
        Directory.CreateDirectory(Path.GetDirectoryName(ledgerPath)!);
        File.WriteAllText(ledgerPath, "{not valid json" + Environment.NewLine);
        ProcessedPackageLedger ledger = new(ledgerPath);

        Assert.False(ledger.HasSent(CreatePackage("pkg", "hash")));
    }

    private static string CreateLedgerPath()
    {
        return Path.Combine(Path.GetTempPath(), "healthmailer-ledger-" + Guid.NewGuid().ToString("N"), "processed-ledger.jsonl");
    }

    private static DeliveryPackage CreatePackage(string packageId, string completedPackageHash)
    {
        return new DeliveryPackage
        {
            PackageDirectory = Path.GetTempPath(),
            PackageId = packageId,
            RecipientEmail = "recipient@example.ie",
            RecipientName = "Recipient",
            Subject = "Subject",
            Body = "Body",
            AttachmentPath = Path.Combine(Path.GetTempPath(), "prescription.pdf"),
            PdfSha256 = "pdf-hash",
            CompletedPackageHash = completedPackageHash,
            PatientName = "Patient",
            Mrn = "MRN"
        };
    }

    private static ProcessingResult CreateSentResult(string packageId, string completedPackageHash)
    {
        return new ProcessingResult
        {
            PackageId = packageId,
            Outcome = PackageOutcome.Sent,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            RecipientEmail = "recipient@example.ie",
            PdfSha256 = "pdf-hash",
            CompletedPackageHash = completedPackageHash,
            MailSent = true
        };
    }
}
