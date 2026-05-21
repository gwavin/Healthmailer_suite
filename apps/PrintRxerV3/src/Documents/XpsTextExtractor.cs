using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;

namespace PrintRxerV3.Documents;

public static partial class XpsTextExtractor
{
    public static IReadOnlyList<string> ExtractGlyphText(string xpsPath)
    {
        if (string.IsNullOrWhiteSpace(xpsPath) || !File.Exists(xpsPath))
        {
            return [];
        }

        List<string> values = [];
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(xpsPath);
            foreach (ZipArchiveEntry entry in archive.Entries.Where(entry => entry.FullName.EndsWith(".fpage", StringComparison.OrdinalIgnoreCase)))
            {
                using Stream stream = entry.Open();
                using StreamReader reader = new(stream);
                string page = reader.ReadToEnd();
                foreach (Match match in UnicodeStringRegex().Matches(page))
                {
                    string value = WebUtility.HtmlDecode(match.Groups["value"].Value).Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        values.Add(Regex.Replace(value, @"\s+", " "));
                    }
                }
            }
        }
        catch
        {
            return [];
        }

        return values;
    }

    [GeneratedRegex("UnicodeString=\"(?<value>[^\"]*)\"", RegexOptions.CultureInvariant)]
    private static partial Regex UnicodeStringRegex();
}
