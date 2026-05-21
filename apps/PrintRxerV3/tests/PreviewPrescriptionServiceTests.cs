using PrintRxerV3.App;
using PrintRxerV3.Capture;
using Xunit;

namespace PrintRxerV3.Tests;

public sealed class PreviewPrescriptionServiceTests
{
    [Fact]
    public void PreparePreview_writes_pdf_under_configured_temp_root()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-preview-" + Guid.NewGuid().ToString("N"));
        string payloadPath = Path.Combine(root, "capture", "job.xps");
        Directory.CreateDirectory(Path.GetDirectoryName(payloadPath)!);
        File.WriteAllText(payloadPath, "payload");
        CapturedPrintJobContext context = new()
        {
            CaptureDirectory = Path.GetDirectoryName(payloadPath)!,
            PayloadPath = payloadPath,
            DocumentName = "Preview test",
            PrinterName = "printRxer",
            PrintJobId = "1",
            SubmittingUser = "tester",
            CapturedAtUtc = DateTimeOffset.UtcNow
        };

        string previewPath = PreviewPrescriptionService.PreparePreviewPdf(
            context,
            Path.Combine(root, "temp"),
            (_, outputPath) => File.WriteAllText(outputPath, "%PDF-1.4\n% preview\n"));

        Assert.StartsWith(Path.Combine(root, "temp"), previewPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".pdf", previewPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(previewPath));
    }

    [Fact]
    public void PreparePreview_does_not_create_output_when_renderer_fails()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-preview-fail-" + Guid.NewGuid().ToString("N"));
        string payloadPath = Path.Combine(root, "capture", "job.xps");
        Directory.CreateDirectory(Path.GetDirectoryName(payloadPath)!);
        File.WriteAllText(payloadPath, "payload");
        CapturedPrintJobContext context = new()
        {
            CaptureDirectory = Path.GetDirectoryName(payloadPath)!,
            PayloadPath = payloadPath,
            DocumentName = "Preview test",
            PrinterName = "printRxer",
            PrintJobId = "1",
            SubmittingUser = "tester",
            CapturedAtUtc = DateTimeOffset.UtcNow
        };

        Assert.Throws<InvalidOperationException>(() => PreviewPrescriptionService.PreparePreviewPdf(
            context,
            Path.Combine(root, "temp"),
            (_, _) => throw new InvalidOperationException("renderer failed")));

        Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "temp"), "*.pdf", SearchOption.AllDirectories));
    }
}
