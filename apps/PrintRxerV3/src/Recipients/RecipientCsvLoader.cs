using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using PrintRxerV3.Common;

namespace PrintRxerV3.Recipients;

public sealed class RecipientCsvOptions
{
    public string? NameColumn { get; init; }
    public string? EmailColumn { get; init; }
    public string? ActiveColumn { get; init; }
    public string? MatchTermsColumn { get; init; }
    public string AliasSeparator { get; init; } = ";";
}

public static class RecipientCsvLoader
{
    public static IReadOnlyList<RecipientRecord> Load(string csvPath, RecipientCsvOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(csvPath))
        {
            throw new ArgumentException("CSV path is required.", nameof(csvPath));
        }

        if (!File.Exists(csvPath))
        {
            throw new FileNotFoundException("Recipient CSV file not found.", csvPath);
        }

        options ??= new RecipientCsvOptions();
        using StreamReader reader = new(csvPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string[]? headers = ReadCsvRow(reader);
        if (headers is null || headers.Length == 0)
        {
            return Array.Empty<RecipientRecord>();
        }

        Dictionary<string, int> indexByHeader = BuildHeaderIndex(headers);
        string? nameColumn = ResolveColumnName(indexByHeader, options.NameColumn, "RecipientName", "Name", "Display Name", "Full Name", "Company");
        string? emailColumn = ResolveColumnName(indexByHeader, options.EmailColumn, "EmailAddress", "E-mail Address", "Healthmail Email", "Email", "Email Address");
        string? activeColumn = ResolveColumnName(indexByHeader, options.ActiveColumn, "Active");
        string? matchTermsColumn = ResolveColumnName(indexByHeader, options.MatchTermsColumn, "MatchTerms", "Match Terms", "Aliases", "Alias");
        List<string> searchColumns = BuildSearchColumns(indexByHeader, nameColumn, emailColumn, activeColumn, matchTermsColumn);
        List<RecipientRecord> recipients = new();

        string[]? fields;
        while ((fields = ReadCsvRow(reader)) is not null)
        {
            string activeValue = GetField(fields, indexByHeader, activeColumn);
            if (IsInactive(activeValue))
            {
                continue;
            }

            string name = TextUtilities.NormalizeWhitespace(GetField(fields, indexByHeader, nameColumn));
            string email = TextUtilities.NormalizeWhitespace(GetField(fields, indexByHeader, emailColumn));
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
            {
                continue;
            }

            HashSet<string> aliases = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> searchTerms = new(StringComparer.OrdinalIgnoreCase);
            AddValue(searchTerms, name);
            AddValue(searchTerms, email);

            foreach (string alias in SplitAliases(GetField(fields, indexByHeader, matchTermsColumn), options.AliasSeparator))
            {
                AddValue(aliases, alias);
                AddValue(searchTerms, alias);
            }

            foreach (string columnName in searchColumns)
            {
                AddValue(searchTerms, GetField(fields, indexByHeader, columnName));
            }

            recipients.Add(new RecipientRecord
            {
                RecipientName = name,
                EmailAddress = email,
                Aliases = aliases.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                SearchTerms = searchTerms.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                SearchText = NormalizeForSearch(name + " " + email + " " + string.Join(" ", searchTerms))
            });
        }

        return recipients;
    }

    private static Dictionary<string, int> BuildHeaderIndex(IReadOnlyList<string> headers)
    {
        Dictionary<string, int> indexByHeader = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < headers.Count; index++)
        {
            string header = TextUtilities.NormalizeWhitespace(headers[index]);
            if (!string.IsNullOrWhiteSpace(header) && !indexByHeader.ContainsKey(header))
            {
                indexByHeader.Add(header, index);
            }
        }

        return indexByHeader;
    }

    private static string? ResolveColumnName(Dictionary<string, int> indexByHeader, string? configuredName, params string[] fallbackNames)
    {
        if (!string.IsNullOrWhiteSpace(configuredName) && indexByHeader.ContainsKey(configuredName))
        {
            return configuredName;
        }

        foreach (string fallbackName in fallbackNames)
        {
            if (indexByHeader.ContainsKey(fallbackName))
            {
                return fallbackName;
            }
        }

        return configuredName;
    }

    private static List<string> BuildSearchColumns(Dictionary<string, int> indexByHeader, params string?[] excludedColumns)
    {
        HashSet<string> excluded = new(StringComparer.OrdinalIgnoreCase);
        foreach (string? excludedColumn in excludedColumns)
        {
            if (!string.IsNullOrWhiteSpace(excludedColumn))
            {
                excluded.Add(excludedColumn);
            }
        }

        excluded.Add("Anniversary");
        excluded.Add("Birthday");
        excluded.Add("Categories");
        return indexByHeader.Keys
            .Where(column => !excluded.Contains(column))
            .OrderBy(column => column, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsInactive(string value)
    {
        string normalized = value.Trim().ToUpperInvariant();
        return normalized is "0" or "FALSE" or "NO";
    }

    private static IEnumerable<string> SplitAliases(string rawAliases, string separator)
    {
        if (string.IsNullOrWhiteSpace(rawAliases))
        {
            yield break;
        }

        separator = string.IsNullOrWhiteSpace(separator) ? ";" : separator;
        foreach (string alias in rawAliases.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            yield return alias;
        }
    }

    private static void AddValue(HashSet<string> values, string? rawValue)
    {
        string value = TextUtilities.NormalizeWhitespace(rawValue).Trim('"');
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(value);
        }
    }

    private static string GetField(string[] fields, Dictionary<string, int> indexByHeader, string? columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName) || !indexByHeader.TryGetValue(columnName, out int index) || index < 0 || index >= fields.Length)
        {
            return string.Empty;
        }

        return fields[index];
    }

    private static string NormalizeForSearch(string value)
    {
        return Regex.Replace(TextUtilities.NormalizeWhitespace(value), @"\s+", " ").ToLowerInvariant();
    }

    private static string[]? ReadCsvRow(TextReader reader)
    {
        List<string> fields = new();
        StringBuilder field = new();
        bool inQuotes = false;
        bool readAny = false;

        while (true)
        {
            int next = reader.Read();
            if (next < 0)
            {
                if (!readAny && field.Length == 0 && fields.Count == 0)
                {
                    return null;
                }

                fields.Add(field.ToString());
                return fields.ToArray();
            }

            readAny = true;
            char ch = (char)next;
            if (ch == '"')
            {
                if (inQuotes && reader.Peek() == '"')
                {
                    reader.Read();
                    field.Append('"');
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                fields.Add(field.ToString());
                field.Clear();
                continue;
            }

            if ((ch == '\n' || ch == '\r') && !inQuotes)
            {
                if (ch == '\r' && reader.Peek() == '\n')
                {
                    reader.Read();
                }

                fields.Add(field.ToString());
                return fields.ToArray();
            }

            field.Append(ch);
        }
    }

    public static IReadOnlyList<RecipientRecord> LoadAny(string path, RecipientCsvOptions? options = null)
    {
        string extension = Path.GetExtension(path);
        if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return Load(path, options);
        }

        if (extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase))
        {
            return LoadRows(ReadWorkbookRows(path), options);
        }

        throw new NotSupportedException("Unsupported recipient file type: " + extension);
    }

    private static IReadOnlyList<RecipientRecord> LoadRows(IReadOnlyList<string[]> rows, RecipientCsvOptions? options)
    {
        if (rows.Count == 0)
        {
            return Array.Empty<RecipientRecord>();
        }

        string temp = Path.Combine(Path.GetTempPath(), "printrxer-v3-recips-" + Guid.NewGuid().ToString("N") + ".csv");
        File.WriteAllLines(temp, rows.Select(row => string.Join(",", row.Select(EscapeCsv))));
        try
        {
            return Load(temp, options);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private static string EscapeCsv(string value)
    {
        value ??= string.Empty;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static List<string[]> ReadWorkbookRows(string path)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        Dictionary<int, string> sharedStrings = ReadSharedStrings(archive);
        ZipArchiveEntry workbookEntry = archive.GetEntry("xl/workbook.xml") ?? throw new InvalidOperationException("Workbook is missing xl/workbook.xml.");
        XDocument workbook = LoadXml(workbookEntry);
        XElement? firstSheet = workbook.Descendants().FirstOrDefault(element => element.Name.LocalName == "sheet");
        string? relationshipId = firstSheet?.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "id")?.Value;
        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            return new List<string[]>();
        }

        XDocument relationships = LoadXml(archive.GetEntry("xl/_rels/workbook.xml.rels") ?? throw new InvalidOperationException("Workbook is missing relationships."));
        XElement? relationship = relationships.Descendants().FirstOrDefault(element => string.Equals((string?)element.Attribute("Id"), relationshipId, StringComparison.OrdinalIgnoreCase));
        string target = relationship?.Attribute("Target")?.Value ?? "worksheets/sheet1.xml";
        string worksheetPath = "xl/" + target.TrimStart('/');
        XDocument worksheet = LoadXml(archive.GetEntry(worksheetPath) ?? throw new InvalidOperationException("Workbook is missing worksheet: " + worksheetPath));
        List<string[]> rows = new();
        foreach (XElement row in worksheet.Descendants().Where(element => element.Name.LocalName == "row"))
        {
            Dictionary<int, string> values = new();
            foreach (XElement cell in row.Elements().Where(element => element.Name.LocalName == "c"))
            {
                int index = GetColumnIndex((string?)cell.Attribute("r"));
                if (index < 0)
                {
                    index = values.Count;
                }

                values[index] = ReadCellValue(cell, sharedStrings);
            }

            if (values.Count == 0)
            {
                continue;
            }

            string[] rowValues = new string[values.Keys.Max() + 1];
            foreach (KeyValuePair<int, string> value in values)
            {
                rowValues[value.Key] = value.Value;
            }

            rows.Add(rowValues);
        }

        return rows;
    }

    private static Dictionary<int, string> ReadSharedStrings(ZipArchive archive)
    {
        ZipArchiveEntry? entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return new Dictionary<int, string>();
        }

        XDocument document = LoadXml(entry);
        return document.Descendants()
            .Where(element => element.Name.LocalName == "si")
            .Select((element, index) => new { index, value = string.Concat(element.Descendants().Where(child => child.Name.LocalName == "t").Select(child => child.Value)) })
            .ToDictionary(item => item.index, item => item.value);
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static string ReadCellValue(XElement cell, Dictionary<int, string> sharedStrings)
    {
        string type = (string?)cell.Attribute("t") ?? string.Empty;
        if (type.Equals("inlineStr", StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat(cell.Descendants().Where(element => element.Name.LocalName == "t").Select(element => element.Value));
        }

        string raw = cell.Elements().FirstOrDefault(element => element.Name.LocalName == "v")?.Value ?? string.Empty;
        if (type.Equals("s", StringComparison.OrdinalIgnoreCase) && int.TryParse(raw, out int sharedStringIndex) && sharedStrings.TryGetValue(sharedStringIndex, out string? value))
        {
            return value;
        }

        return raw;
    }

    private static int GetColumnIndex(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return -1;
        }

        int column = 0;
        bool found = false;
        foreach (char ch in reference.ToUpperInvariant())
        {
            if (ch < 'A' || ch > 'Z')
            {
                break;
            }

            found = true;
            column = (column * 26) + (ch - 'A' + 1);
        }

        return found ? column - 1 : -1;
    }
}
