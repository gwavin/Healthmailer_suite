using System.Security.Principal;
using System.Runtime.Versioning;
using System.Text;
using PrintRxerV3.Handoff;
using PrintRxerV3.Metadata;
using PrintRxerV3.Packaging;

namespace PrintRxerV3.App;

public static class PreviewPackageCreator
{
    [SupportedOSPlatform("windows")]
    public static string CreateSamplePackage(string outputRoot)
    {
        Directory.CreateDirectory(outputRoot);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string packageId = PackageIdGenerator.Create(now);
        string workingDirectory = Path.Combine(Path.GetTempPath(), "printrxer-v3-preview-" + packageId);
        Directory.CreateDirectory(workingDirectory);

        string pdfPath = Path.Combine(workingDirectory, "prescription.pdf");
        File.WriteAllBytes(pdfPath, CreateMinimalPdf());

        string pdfSha256 = Sha256Hasher.HashFile(pdfPath);
        PackageRequest request = PackageRequestBuilder.Create(
            packageId,
            pdfSha256,
            now,
            CaptureWorkstationIdentity(),
            new PrintJobOrigin
            {
                Source = "preview-cli",
                PrinterName = "printrxer_v3 preview",
                DocumentName = "Sample prescription preview",
                PrintJobId = "preview",
                CapturedAtUtc = now,
                SubmittingUser = WindowsIdentity.GetCurrent().Name,
                SubmittingUserSid = WindowsIdentity.GetCurrent().User?.Value,
                SubmittingSessionId = Environment.UserInteractive ? System.Diagnostics.Process.GetCurrentProcess().SessionId : null
            },
            new PickerSelection
            {
                RecipientName = "Sample HealthMailer Recipient",
                RecipientEmail = "sample.recipient@example.invalid",
                Subject = "Sample prescription handoff",
                Body = "This is a PrintRxer v3 preview package. HealthMailer would own downstream delivery.",
                SelectedAt = now
            });

        return HandoffPackageWriter.Write(outputRoot, request, pdfPath);
    }

    [SupportedOSPlatform("windows")]
    private static WorkstationIdentity CaptureWorkstationIdentity()
    {
        WindowsIdentity identity = WindowsIdentity.GetCurrent();
        string domainUser = identity.Name ?? string.Empty;
        string windowsUser = domainUser.Contains('\\') ? domainUser.Split('\\').Last() : domainUser;
        string workstationDomain = Environment.UserDomainName ?? string.Empty;

        return new WorkstationIdentity
        {
            WindowsUser = windowsUser,
            DomainUser = domainUser,
            UserSid = identity.User?.Value ?? string.Empty,
            SessionId = System.Diagnostics.Process.GetCurrentProcess().SessionId,
            WorkstationName = Environment.MachineName,
            WorkstationDomain = workstationDomain
        };
    }

    private static byte[] CreateMinimalPdf()
    {
        const string pdf = """
            %PDF-1.4
            1 0 obj
            << /Type /Catalog /Pages 2 0 R >>
            endobj
            2 0 obj
            << /Type /Pages /Kids [3 0 R] /Count 1 >>
            endobj
            3 0 obj
            << /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>
            endobj
            4 0 obj
            << /Length 88 >>
            stream
            BT /F1 18 Tf 72 720 Td (PrintRxer v3 preview prescription package) Tj ET
            endstream
            endobj
            5 0 obj
            << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>
            endobj
            xref
            0 6
            0000000000 65535 f
            0000000010 00000 n
            0000000059 00000 n
            0000000116 00000 n
            0000000232 00000 n
            0000000370 00000 n
            trailer
            << /Size 6 /Root 1 0 R >>
            startxref
            440
            %%EOF
            """;

        return Encoding.ASCII.GetBytes(pdf.Replace("\r\n", "\n"));
    }
}
