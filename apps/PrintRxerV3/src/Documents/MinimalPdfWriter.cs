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

    public static void Write(string pdfPath, IReadOnlyList<PdfPageImage> pages)
    {
        if (pages.Count == 0)
        {
            throw new InvalidOperationException("The rendered PDF would contain no pages.");
        }

        List<PdfObject> objects = [new PdfObject(), new PdfObject()];
        List<int> pageObjectIds = [];

        for (int i = 0; i < pages.Count; i++)
        {
            PdfPageImage page = pages[i];
            int imageObjectId = AddObject(objects, new PdfObject
            {
                Dictionary = string.Format(
                    CultureInfo.InvariantCulture,
                    "<< /Type /XObject /Subtype /Image /Width {0} /Height {1} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {2} >>",
                    page.PixelWidth,
                    page.PixelHeight,
                    page.ImageBytes.Length),
                StreamData = page.ImageBytes
            });

            string content = string.Format(
                CultureInfo.InvariantCulture,
                "q {0:0.###} 0 0 {1:0.###} 0 0 cm /Im{2} Do Q",
                page.WidthPoints,
                page.HeightPoints,
                i + 1);

            int contentObjectId = AddObject(objects, new PdfObject
            {
                Dictionary = string.Format(CultureInfo.InvariantCulture, "<< /Length {0} >>", Encoding.ASCII.GetByteCount(content)),
                StreamData = Encoding.ASCII.GetBytes(content)
            });

            int pageObjectId = AddObject(objects, new PdfObject
            {
                PlainContent = string.Format(
                    CultureInfo.InvariantCulture,
                    "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {0:0.###} {1:0.###}] /Resources << /XObject << /Im{2} {3} 0 R >> >> /Contents {4} 0 R >>",
                    page.WidthPoints,
                    page.HeightPoints,
                    i + 1,
                    imageObjectId,
                    contentObjectId)
            });

            pageObjectIds.Add(pageObjectId);
        }

        objects[0] = new PdfObject { PlainContent = "<< /Type /Catalog /Pages 2 0 R >>" };
        objects[1] = new PdfObject
        {
            PlainContent = string.Format(
                CultureInfo.InvariantCulture,
                "<< /Type /Pages /Count {0} /Kids [{1}] >>",
                pageObjectIds.Count,
                string.Join(" ", pageObjectIds.Select(id => id.ToString(CultureInfo.InvariantCulture) + " 0 R")))
        };

        using FileStream stream = new(pdfPath, FileMode.Create, FileAccess.Write);
        List<long> offsets = [];
        WriteAscii(stream, "%PDF-1.4\n");
        WriteBytes(stream, [0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A]);

        for (int objectIndex = 0; objectIndex < objects.Count; objectIndex++)
        {
            offsets.Add(stream.Position);
            WriteAscii(stream, string.Format(CultureInfo.InvariantCulture, "{0} 0 obj\n", objectIndex + 1));
            PdfObject obj = objects[objectIndex];
            if (obj.StreamData is not null)
            {
                WriteAscii(stream, obj.Dictionary + "\nstream\n");
                WriteBytes(stream, obj.StreamData);
                WriteAscii(stream, "\nendstream\nendobj\n");
            }
            else
            {
                WriteAscii(stream, obj.PlainContent + "\nendobj\n");
            }
        }

        long xrefOffset = stream.Position;
        WriteAscii(stream, string.Format(CultureInfo.InvariantCulture, "xref\n0 {0}\n", objects.Count + 1));
        WriteAscii(stream, "0000000000 65535 f \n");
        foreach (long offset in offsets)
        {
            WriteAscii(stream, offset.ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n");
        }

        WriteAscii(stream, string.Format(
            CultureInfo.InvariantCulture,
            "trailer\n<< /Size {0} /Root 1 0 R >>\nstartxref\n{1}\n%%EOF",
            objects.Count + 1,
            xrefOffset));
    }

    private static int AddObject(List<PdfObject> objects, PdfObject obj)
    {
        objects.Add(obj);
        return objects.Count;
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
