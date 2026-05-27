using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;

namespace PrintRxerV3.Documents;

public static partial class XpsTextExtractor
{
    private const long MaxPageXmlBytes = 2_000_000;
    private const int MaxPages = 200;
    private const int MaxTotalTextChars = 200_000;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex UnicodeStringPattern = new("UnicodeString=\"(?<value>[^\"]*)\"", RegexOptions.CultureInvariant, RegexTimeout);

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
            int pages = 0;
            int totalChars = 0;
            foreach (ZipArchiveEntry entry in archive.Entries.Where(entry => entry.FullName.EndsWith(".fpage", StringComparison.OrdinalIgnoreCase)))
            {
                pages++;
                if (pages > MaxPages || entry.Length > MaxPageXmlBytes)
                {
                    return [];
                }

                using Stream stream = entry.Open();
                using StreamReader reader = new(stream);
                string page = reader.ReadToEnd();
                if (page.Length > MaxPageXmlBytes)
                {
                    return [];
                }

                foreach (Match match in UnicodeStringPattern.Matches(page))
                {
                    string value = WebUtility.HtmlDecode(match.Groups["value"].Value).Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        string normalized = Regex.Replace(value, @"\s+", " ", RegexOptions.None, RegexTimeout);
                        totalChars += normalized.Length;
                        if (totalChars > MaxTotalTextChars)
                        {
                            return [];
                        }

                        values.Add(normalized);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or RegexMatchTimeoutException)
        {
            return [];
        }

        return values;
    }
}
