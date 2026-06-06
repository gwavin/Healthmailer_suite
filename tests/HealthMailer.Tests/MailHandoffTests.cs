using System.Reflection;

namespace HealthMailer.Tests;

public sealed class MailHandoffTests
{
    [Test]
    public void AttachmentFilePreparer_creates_friendly_temp_copy_and_cleans_up()
    {
        string root = Path.Combine(Path.GetTempPath(), "healthmailer-attachment-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string internalPdf = Path.Combine(root, "prescription.pdf");
        File.WriteAllText(internalPdf, "%PDF-1.4\n% test\n");
        DeliveryPackage package = CreatePackage(internalPdf) with { AttachmentDisplayName = "MRN123_prescription_20260526_1430.pdf" };

        using PreparedAttachment prepared = AttachmentFilePreparer.Prepare(package);
        string preparedDirectory = Path.GetDirectoryName(prepared.Path)!;

        Assert.Equal("MRN123_prescription_20260526_1430.pdf", Path.GetFileName(prepared.Path));
        Assert.True(File.Exists(prepared.Path));
        Assert.Equal(internalPdf, package.AttachmentPath);

        prepared.Dispose();

        Assert.False(Directory.Exists(preparedDirectory));
    }


    [TestCase("..\\..\\bad.exe")]
    [TestCase("C:\\Temp\\bad.pdf")]
    [TestCase("")]
    public void AttachmentFilePreparer_sanitises_invalid_display_names_inside_temp_folder(string displayName)
    {
        string root = Path.Combine(Path.GetTempPath(), "healthmailer-attachment-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string internalPdf = Path.Combine(root, "prescription.pdf");
        File.WriteAllText(internalPdf, "%PDF-1.4\n% test\n");
        DeliveryPackage package = CreatePackage(internalPdf) with { AttachmentDisplayName = displayName };

        using PreparedAttachment prepared = AttachmentFilePreparer.Prepare(package);

        Assert.EndsWith(".pdf", prepared.Path, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(Path.Combine(Path.GetTempPath(), "HealthMailer"), Path.GetFullPath(prepared.Path), StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(prepared.Path));
    }

    [Test]
    public void ResolveRecipients_throws_when_outlook_resolve_all_returns_false()
    {
        MethodInfo method = typeof(OutlookMailHandoff).GetMethod("ResolveRecipients", BindingFlags.NonPublic | BindingFlags.Static)!;

        TargetInvocationException ex = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [new FakeMailItem(false)]));

        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("could not resolve", ex.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public void ResolveRecipients_allows_true_resolve_all_result()
    {
        MethodInfo method = typeof(OutlookMailHandoff).GetMethod("ResolveRecipients", BindingFlags.NonPublic | BindingFlags.Static)!;

        method.Invoke(null, [new FakeMailItem(true)]);
    }

    public sealed class FakeMailItem(bool resolveResult)
    {
        public FakeRecipients Recipients { get; } = new(resolveResult);
    }

    public sealed class FakeRecipients(bool resolveResult)
    {
        public bool ResolveAll() => resolveResult;
    }

    private static DeliveryPackage CreatePackage(string internalPdf)
    {
        return new DeliveryPackage
        {
            PackageDirectory = Path.GetDirectoryName(internalPdf)!,
            PackageId = "pkg-123",
            RecipientEmail = "recipient@healthmail.ie",
            RecipientName = "Recipient",
            Subject = "Prescription",
            Body = "Please see attached.",
            AttachmentPath = internalPdf,
            PdfSha256 = "hash",
            CompletedPackageHash = "completed",
            DocumentKind = "Prescription",
            DocumentName = "Prescription",
            AttachmentDisplayName = "prescription.pdf"
        };
    }
}
