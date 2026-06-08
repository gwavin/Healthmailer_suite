using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Xps.Packaging;

namespace PrintRxerV3.Documents;

public static partial class XpsPdfRenderer
{
    public static IReadOnlyList<byte[]> RenderToJpegPages(
        string xpsPath,
        int dpi = 150,
        int jpegQuality = 85,
        int maxPageCount = 100,
        long maxRenderedPixelsPerPage = 50_000_000)
    {
        if (string.IsNullOrWhiteSpace(xpsPath) || !File.Exists(xpsPath))
        {
            throw new FileNotFoundException("Captured XPS file not found.", xpsPath);
        }

        string renderPath = xpsPath;
        string? normalizedPath = null;
        try
        {
            if (IsPiecewiseXpsPackage(xpsPath))
            {
                normalizedPath = NormalizePiecewiseXpsPackage(xpsPath);
                renderPath = normalizedPath;
            }

            return RenderPages(renderPath, dpi, jpegQuality, maxPageCount, maxRenderedPixelsPerPage)
                .Select(page => page.ImageBytes)
                .ToArray();
        }
        finally
        {
            if (normalizedPath is not null)
            {
                TryDelete(normalizedPath);
            }
        }
    }

    public static void RenderToPdf(
        string xpsPath,
        string pdfPath,
        int dpi = 150,
        int jpegQuality = 85,
        int maxPageCount = 100,
        long maxRenderedPixelsPerPage = 50_000_000)
    {
        if (string.IsNullOrWhiteSpace(xpsPath) || !File.Exists(xpsPath))
        {
            throw new FileNotFoundException("Captured XPS file not found.", xpsPath);
        }

        string renderPath = xpsPath;
        string? normalizedPath = null;
        try
        {
            if (IsPiecewiseXpsPackage(xpsPath))
            {
                normalizedPath = NormalizePiecewiseXpsPackage(xpsPath);
                renderPath = normalizedPath;
            }

            MinimalPdfWriter.Write(pdfPath, RenderPages(renderPath, dpi, jpegQuality, maxPageCount, maxRenderedPixelsPerPage));
        }
        finally
        {
            if (normalizedPath is not null)
            {
                TryDelete(normalizedPath);
            }
        }
    }

    private static IEnumerable<PdfPageImage> RenderPages(
        string renderPath,
        int dpi,
        int jpegQuality,
        int maxPageCount,
        long maxRenderedPixelsPerPage)
    {
        using XpsDocument xpsDocument = new(renderPath, FileAccess.Read);
        FixedDocumentSequence sequence = xpsDocument.GetFixedDocumentSequence();
        if (sequence is null)
        {
            throw new InvalidDataException("The XPS document does not contain a readable FixedDocumentSequence.");
        }

        DocumentPaginator paginator = ((IDocumentPaginatorSource)sequence).DocumentPaginator;
        if (!paginator.IsPageCountValid)
        {
            paginator.ComputePageCount();
        }

        if (maxPageCount > 0 && paginator.PageCount > maxPageCount)
        {
            throw new InvalidOperationException("The XPS document has " + paginator.PageCount.ToString(CultureInfo.InvariantCulture) + " pages, which exceeds the configured maximum of " + maxPageCount.ToString(CultureInfo.InvariantCulture) + ".");
        }

        for (int pageIndex = 0; pageIndex < paginator.PageCount; pageIndex++)
        {
            DocumentPage page = paginator.GetPage(pageIndex);
            Size size = page.Size;
            int pixelWidth = Math.Max(1, (int)Math.Ceiling(size.Width / 96.0 * dpi));
            int pixelHeight = Math.Max(1, (int)Math.Ceiling(size.Height / 96.0 * dpi));
            long renderedPixels = (long)pixelWidth * pixelHeight;
            if (maxRenderedPixelsPerPage > 0 && renderedPixels > maxRenderedPixelsPerPage)
            {
                throw new InvalidOperationException("XPS page " + (pageIndex + 1).ToString(CultureInfo.InvariantCulture) + " would render to " + renderedPixels.ToString(CultureInfo.InvariantCulture) + " pixels, which exceeds the configured maximum of " + maxRenderedPixelsPerPage.ToString(CultureInfo.InvariantCulture) + ".");
            }

            RenderTargetBitmap bitmap = new(pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
            DrawingVisual visual = new();
            using (DrawingContext drawingContext = visual.RenderOpen())
            {
                drawingContext.DrawRectangle(Brushes.White, null, new Rect(0, 0, size.Width, size.Height));
                drawingContext.DrawRectangle(new VisualBrush(page.Visual), null, new Rect(0, 0, size.Width, size.Height));
            }

            bitmap.Render(visual);
            JpegBitmapEncoder encoder = new()
            {
                QualityLevel = Math.Max(1, Math.Min(100, jpegQuality))
            };
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using MemoryStream imageStream = new();
            encoder.Save(imageStream);
            yield return new PdfPageImage
            {
                ImageBytes = imageStream.ToArray(),
                PixelWidth = pixelWidth,
                PixelHeight = pixelHeight,
                WidthPoints = size.Width * 72.0 / 96.0,
                HeightPoints = size.Height * 72.0 / 96.0
            };
        }
    }

    private static bool IsPiecewiseXpsPackage(string xpsPath)
    {
        using ZipArchive archive = ZipFile.OpenRead(xpsPath);
        return archive.Entries.Any(entry => PieceEntryRegex().IsMatch(entry.FullName));
    }

    private static string NormalizePiecewiseXpsPackage(string xpsPath)
    {
        string tempPath = Path.Combine(Path.GetTempPath(), "printrxer-v3-" + Guid.NewGuid().ToString("N") + ".xps");
        using ZipArchive source = ZipFile.OpenRead(xpsPath);
        using ZipArchive destination = ZipFile.Open(tempPath, ZipArchiveMode.Create);

        HashSet<string> piecedNames = source.Entries
            .Select(entry => PieceEntryRegex().Match(entry.FullName))
            .Where(match => match.Success)
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (ZipArchiveEntry entry in source.Entries)
        {
            if (PieceEntryRegex().IsMatch(entry.FullName) || piecedNames.Contains(entry.FullName))
            {
                continue;
            }

            CopyEntry(entry, destination.CreateEntry(entry.FullName, CompressionLevel.Optimal));
        }

        foreach (IGrouping<string, ZipArchiveEntry> group in source.Entries
            .Select(entry => new { Entry = entry, Match = PieceEntryRegex().Match(entry.FullName) })
            .Where(item => item.Match.Success)
            .GroupBy(item => item.Match.Groups["name"].Value, item => item.Entry, StringComparer.OrdinalIgnoreCase))
        {
            ZipArchiveEntry normalized = destination.CreateEntry(group.Key, CompressionLevel.Optimal);
            using Stream output = normalized.Open();
            foreach (ZipArchiveEntry piece in group.OrderBy(entry => int.Parse(PieceEntryRegex().Match(entry.FullName).Groups["index"].Value, CultureInfo.InvariantCulture)))
            {
                using Stream input = piece.Open();
                input.CopyTo(output);
            }
        }

        return tempPath;
    }

    private static void CopyEntry(ZipArchiveEntry source, ZipArchiveEntry destination)
    {
        using Stream input = source.Open();
        using Stream output = destination.Open();
        input.CopyTo(output);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    [GeneratedRegex(@"^(?<name>.+)/\[(?<index>\d+)\](?:\.last)?\.piece$", RegexOptions.CultureInvariant)]
    private static partial Regex PieceEntryRegex();
}
