using PrintRxerV3.App;
using PrintRxerV3.Capture;

namespace PrintRxerV3.Tests;

public sealed class PreviewPrescriptionServiceTests
{
    [Test]
    public void Preview_uses_controlled_modal_viewer_instead_of_default_pdf_application()
    {
        string root = FindRepoRoot();
        string program = File.ReadAllText(Path.Combine(root, "apps", "PrintRxerV3", "app", "Program.cs"));
        string dialog = File.ReadAllText(Path.Combine(root, "apps", "PrintRxerV3", "app", "RecipientSelectionDialog.cs"));
        string previewForm = File.ReadAllText(Path.Combine(root, "apps", "PrintRxerV3", "app", "DocumentPreviewForm.cs"));

        Assert.Contains("DocumentPreviewForm", program);
        Assert.DoesNotContain("OpenWithDefaultViewer(previewPath)", program);
        Assert.Contains("TopMost = false", dialog);
        Assert.Contains("TopMost = previousTopMost", dialog);
        Assert.Contains("preview.ShowDialog()", program);
        Assert.Contains("\"Close\"", previewForm);
        Assert.Contains("\"Previous page\"", previewForm);
        Assert.Contains("\"Next page\"", previewForm);
        Assert.Contains("\"Fit\"", previewForm);
        Assert.DoesNotContain("WebView", previewForm, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Process.Start", previewForm, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SaveFileDialog", previewForm, StringComparison.OrdinalIgnoreCase);
    }

    [Test]
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

    [Test]
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

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PrintRxerSuite.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
