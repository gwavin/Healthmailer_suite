using System.IO;
using System.Globalization;
using System.Text;

namespace PrintRxerV3.Documents;

internal static class MinimalPdfWriter
{
    private sealed class PdfObject
    {
        public string? Dictionary { get; init; }
        public byte[]? StreamData { get; init; }
        public string? PlainContent { get; init; }
    }

    public static void Write(string pdfPath, IEnumerable<PdfPageImage> pages)
    {
        using FileStream stream = new(pdfPath, FileMode.Create, FileAccess.Write);
        List<long> offsets = [];
        WriteAscii(stream, "%PDF-1.4\n");
        WriteBytes(stream, [0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A]);

        int objectIdCounter = 1;
        // Reserve objects 1 (Catalog) and 2 (Pages)
        offsets.Add(0); // placeholder for 1 0 obj
        offsets.Add(0); // placeholder for 2 0 obj
        objectIdCounter = 3;

        List<int> pageObjectIds = [];

        int pageIndex = 0;
        foreach (PdfPageImage page in pages)
        {
            int i = pageIndex++;
            int imageObjectId = objectIdCounter++;
            offsets.Add(stream.Position);
            WriteAscii(stream, string.Format(CultureInfo.InvariantCulture, "{0} 0 obj\n", imageObjectId));
            string imageDictionary = string.Format(
                CultureInfo.InvariantCulture,
                "<< /Type /XObject /Subtype /Image /Width {0} /Height {1} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {2} >>",
                page.PixelWidth,
                page.PixelHeight,
                page.ImageBytes.Length);
            WriteAscii(stream, imageDictionary + "\nstream\n");
            WriteBytes(stream, page.ImageBytes);
            WriteAscii(stream, "\nendstream\nendobj\n");

            int contentObjectId = objectIdCounter++;
            offsets.Add(stream.Position);
            WriteAscii(stream, string.Format(CultureInfo.InvariantCulture, "{0} 0 obj\n", contentObjectId));
            string content = string.Format(
                CultureInfo.InvariantCulture,
                "q {0:0.###} 0 0 {1:0.###} 0 0 cm /Im{2} Do Q",
                page.WidthPoints,
                page.HeightPoints,
                i + 1);
            string contentDictionary = string.Format(CultureInfo.InvariantCulture, "<< /Length {0} >>", Encoding.ASCII.GetByteCount(content));
            WriteAscii(stream, contentDictionary + "\nstream\n");
            WriteBytes(stream, Encoding.ASCII.GetBytes(content));
            WriteAscii(stream, "\nendstream\nendobj\n");

            int pageObjectId = objectIdCounter++;
            offsets.Add(stream.Position);
            WriteAscii(stream, string.Format(CultureInfo.InvariantCulture, "{0} 0 obj\n", pageObjectId));
            string pageContent = string.Format(
                CultureInfo.InvariantCulture,
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {0:0.###} {1:0.###}] /Resources << /XObject << /Im{2} {3} 0 R >> >> /Contents {4} 0 R >>",
                page.WidthPoints,
                page.HeightPoints,
                i + 1,
                imageObjectId,
                contentObjectId);
            WriteAscii(stream, pageContent + "\nendobj\n");
            pageObjectIds.Add(pageObjectId);
        }

        if (pageObjectIds.Count == 0)
        {
            throw new InvalidOperationException("The rendered PDF would contain no pages.");
        }

        offsets[0] = stream.Position;
        WriteAscii(stream, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        offsets[1] = stream.Position;
        WriteAscii(stream, "2 0 obj\n");
        string pagesContent = string.Format(
            CultureInfo.InvariantCulture,
            "<< /Type /Pages /Count {0} /Kids [{1}] >>",
            pageObjectIds.Count,
            string.Join(" ", pageObjectIds.Select(id => id.ToString(CultureInfo.InvariantCulture) + " 0 R")));
        WriteAscii(stream, pagesContent + "\nendobj\n");

        long xrefOffset = stream.Position;
        WriteAscii(stream, string.Format(CultureInfo.InvariantCulture, "xref\n0 {0}\n", offsets.Count + 1));
        WriteAscii(stream, "0000000000 65535 f \n");
        foreach (long offset in offsets)
        {
            WriteAscii(stream, offset.ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n");
        }

        WriteAscii(stream, string.Format(
            CultureInfo.InvariantCulture,
            "trailer\n<< /Size {0} /Root 1 0 R >>\nstartxref\n{1}\n%%EOF",
            offsets.Count + 1,
            xrefOffset));
    }

    private static void WriteAscii(Stream stream, string value)
    {
        WriteBytes(stream, Encoding.ASCII.GetBytes(value));
    }

    private static void WriteBytes(Stream stream, byte[] value)
    {
        stream.Write(value, 0, value.Length);
    }
}
