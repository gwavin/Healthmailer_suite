using System.IO.Compression;
using PrintRxerV3.Documents;
using Xunit;

namespace PrintRxerV3.Tests;

public sealed class XpsTextExtractorTests
{
    [Fact]
    public void ExtractGlyphText_reads_normal_fpage_unicode_strings()
    {
        string xpsPath = Path.Combine(Path.GetTempPath(), "xps-normal-" + Guid.NewGuid().ToString("N") + ".xps");
        CreateXps(xpsPath, "Documents/1/Pages/1.fpage", "<FixedPage><Glyphs UnicodeString=\"Hello World\" /></FixedPage>");

        IReadOnlyList<string> values = XpsTextExtractor.ExtractGlyphText(xpsPath);

        Assert.Contains("Hello World", values);
    }

    [Fact]
    public void ExtractGlyphText_rejects_oversized_page_without_throwing()
    {
        string xpsPath = Path.Combine(Path.GetTempPath(), "xps-large-" + Guid.NewGuid().ToString("N") + ".xps");
        string largePage = "<FixedPage><Glyphs UnicodeString=\"" + new string('A', 2_100_000) + "\" /></FixedPage>";
        CreateXps(xpsPath, "Documents/1/Pages/1.fpage", largePage);

        IReadOnlyList<string> values = XpsTextExtractor.ExtractGlyphText(xpsPath);

        Assert.Empty(values);
    }

    private static void CreateXps(string path, string entryName, string content)
    {
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry(entryName);
        using StreamWriter writer = new(entry.Open());
        writer.Write(content);
    }
}
