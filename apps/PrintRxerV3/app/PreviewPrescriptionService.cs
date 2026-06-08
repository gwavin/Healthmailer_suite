using System.Diagnostics;
using PrintRxerV3.Capture;
using PrintRxerV3.Documents;

namespace PrintRxerV3.App;

public static class PreviewPrescriptionService
{
    public static string PreparePreviewPdf(
        CapturedPrintJobContext context,
        string tempRoot,
        Action<string, string>? renderPreview = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(context.PayloadPath) || !File.Exists(context.PayloadPath))
        {
            throw new FileNotFoundException("Captured document payload was not available for preview.", context.PayloadPath);
        }

        if (string.IsNullOrWhiteSpace(tempRoot))
        {
            throw new ArgumentException("Preview temp root is required.", nameof(tempRoot));
        }

        string previewDirectory = Path.Combine(tempRoot, "preview-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(previewDirectory);
        string previewPath = Path.Combine(previewDirectory, "prescription-preview.pdf");

        try
        {
            Action<string, string> renderer = renderPreview ?? ((sourcePath, outputPath) => XpsPdfRenderer.RenderToPdf(sourcePath, outputPath));
            renderer(context.PayloadPath, previewPath);
            if (!File.Exists(previewPath))
            {
                throw new InvalidOperationException("Preview PDF was not created.");
            }

            return previewPath;
        }
        catch
        {
            TryDeleteDirectory(previewDirectory);
            throw;
        }
    }

    public static void OpenWithDefaultViewer(string previewPath)
    {
        if (string.IsNullOrWhiteSpace(previewPath) || !File.Exists(previewPath))
        {
            throw new FileNotFoundException("Preview PDF was not available.", previewPath);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = previewPath,
            UseShellExecute = true
        });
    }

    public static IReadOnlyList<byte[]> PreparePreviewPages(CapturedPrintJobContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(context.PayloadPath) || !File.Exists(context.PayloadPath))
        {
            throw new FileNotFoundException("Captured document payload was not available for preview.", context.PayloadPath);
        }

        return XpsPdfRenderer.RenderToJpegPages(context.PayloadPath);
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
        }
    }
}
