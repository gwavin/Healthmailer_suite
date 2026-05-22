using System.Net.Mail;
using System.Text;
using PrintRxerV3.Common;

namespace PrintRxerV3.Recipients;

public static class RecipientCsvValidator
{
    public static IReadOnlyList<RecipientRecord> LoadValidated(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Recipient CSV path is required.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Recipient CSV file not found.", path);
        }

        FileInfo file = new(path);
        if (file.Length == 0)
        {
            throw new InvalidDataException("Recipient CSV is empty.");
        }

        using StreamReader reader = new(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string[] headers = ReadCsvRow(reader) ?? throw new InvalidDataException("Recipient CSV header is missing.");
        Dictionary<string, int> index = BuildHeaderIndex(headers);
        RecipientCsvSchema schema = ResolveSchema(index);

        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        List<RecipientRecord> activeRecipients = new();
        string[]? fields;
        int row = 1;
        while ((fields = ReadCsvRow(reader)) is not null)
        {
            row++;
            if (fields.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            string name = ResolveDisplayName(fields, index, schema);
            string email = TextUtilities.NormalizeWhitespace(Get(fields, index, schema.EmailColumn));
            string id = ResolveRecipientId(fields, index, schema, email);
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidDataException($"Row {row} is missing recipientId.");
            }

            if (!ids.Add(id))
            {
                throw new InvalidDataException($"Duplicate recipientId '{id}' found.");
            }

            if (schema.ActiveColumn is not null)
            {
                string activeText = TextUtilities.NormalizeWhitespace(Get(fields, index, schema.ActiveColumn));
                if (!TryParseBoolean(activeText, out bool active))
                {
                    throw new InvalidDataException($"Row {row} active value is not true/false.");
                }

                if (!active)
                {
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidDataException($"Row {row} active recipient is missing displayName.");
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                throw new InvalidDataException($"Row {row} active recipient is missing email.");
            }

            try
            {
                _ = new MailAddress(email);
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException($"Row {row} email is not plausible: {email}", ex);
            }

            HashSet<string> searchTerms = new(StringComparer.OrdinalIgnoreCase) { id, name, email };
            foreach (string column in schema.SearchColumns)
            {
                AddOptionalSearchTerm(fields, index, searchTerms, column);
            }

            activeRecipients.Add(new RecipientRecord
            {
                RecipientName = name,
                EmailAddress = email,
                Aliases = Array.Empty<string>(),
                SearchTerms = searchTerms.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                SearchText = string.Join(" ", searchTerms).ToLowerInvariant()
            });
        }

        if (activeRecipients.Count == 0)
        {
            throw new InvalidDataException("Recipient CSV must contain at least one active recipient.");
        }

        return activeRecipients;
    }

    private static RecipientCsvSchema ResolveSchema(Dictionary<string, int> index)
    {
        if (index.ContainsKey("recipientId") &&
            index.ContainsKey("displayName") &&
            index.ContainsKey("email") &&
            index.ContainsKey("active"))
        {
            return new RecipientCsvSchema(
                IdColumns: new[] { "recipientId" },
                DisplayNameColumn: "displayName",
                EmailColumn: "email",
                ActiveColumn: "active",
                SearchColumns: new[] { "organisation", "site", "department", "service" });
        }

        if (TryFindColumn(index, out string healthmailAddressColumn, "Healthmail Address", "Healthmail Email") &&
            TryFindColumn(index, out string displayNameColumn, "DisplayName", "Display Name"))
        {
            return new RecipientCsvSchema(
                IdColumns: new[] { "RecipientId", "recipientId", "Healthmail Address", healthmailAddressColumn },
                DisplayNameColumn: displayNameColumn,
                EmailColumn: healthmailAddressColumn,
                ActiveColumn: null,
                SearchColumns: new[] { "Company", "City", "County", "County ", "Phone", "Title" });
        }

        if (TryFindColumn(index, out string outlookEmailColumn, "E-mail Address", "Email Address", "Email") &&
            TryFindColumn(index, out string outlookNameColumn, "Name", "Display Name", "DisplayName"))
        {
            return new RecipientCsvSchema(
                IdColumns: new[] { "recipientId", "RecipientId", "Government ID Number", "ID 2", outlookEmailColumn },
                DisplayNameColumn: outlookNameColumn,
                EmailColumn: outlookEmailColumn,
                ActiveColumn: null,
                SearchColumns: new[] { "Title", "First Name", "Middle Name", "Last Name", "Company", "City", "County", "Business Phone", "Job Title" });
        }

        throw new InvalidDataException("Recipient CSV is missing required columns. Expected recipientId, displayName, email, active or a supported Healthmail/Outlook export shape.");
    }

    private static string ResolveRecipientId(string[] fields, Dictionary<string, int> index, RecipientCsvSchema schema, string email)
    {
        foreach (string column in schema.IdColumns)
        {
            if (!index.ContainsKey(column))
            {
                continue;
            }

            string value = TextUtilities.NormalizeWhitespace(Get(fields, index, column));
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return email.ToLowerInvariant();
    }

    private static string ResolveDisplayName(string[] fields, Dictionary<string, int> index, RecipientCsvSchema schema)
    {
        string value = TextUtilities.NormalizeWhitespace(Get(fields, index, schema.DisplayNameColumn));
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        string[] fallbackParts = new[] { "Title", "First Name", "Middle Name", "Last Name" }
            .Select(column => TextUtilities.NormalizeWhitespace(Get(fields, index, column)))
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        return TextUtilities.NormalizeWhitespace(string.Join(" ", fallbackParts));
    }

    private static void AddOptionalSearchTerm(string[] fields, Dictionary<string, int> index, HashSet<string> terms, string column)
    {
        if (!index.ContainsKey(column))
        {
            return;
        }

        string value = TextUtilities.NormalizeWhitespace(Get(fields, index, column));
        if (!string.IsNullOrWhiteSpace(value))
        {
            terms.Add(value);
        }
    }

    private static string RequireColumn(Dictionary<string, int> index, string name)
    {
        if (!index.ContainsKey(name))
        {
            throw new InvalidDataException("Recipient CSV is missing required column: " + name);
        }

        return name;
    }

    private static bool TryFindColumn(Dictionary<string, int> index, out string column, params string[] names)
    {
        foreach (string name in names)
        {
            if (index.ContainsKey(name))
            {
                column = name;
                return true;
            }
        }

        column = string.Empty;
        return false;
    }

    private static Dictionary<string, int> BuildHeaderIndex(IReadOnlyList<string> headers)
    {
        Dictionary<string, int> index = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Count; i++)
        {
            string header = TextUtilities.NormalizeWhitespace(headers[i]);
            if (!string.IsNullOrWhiteSpace(header) && !index.ContainsKey(header))
            {
                index.Add(header, i);
            }
        }

        return index;
    }

    private static string Get(string[] fields, Dictionary<string, int> index, string column)
    {
        return index.TryGetValue(column, out int fieldIndex) && fieldIndex >= 0 && fieldIndex < fields.Length
            ? fields[fieldIndex]
            : string.Empty;
    }

    private static bool TryParseBoolean(string value, out bool result)
    {
        if (bool.TryParse(value, out result))
        {
            return true;
        }

        if (value == "1" || value.Equals("yes", StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }

        if (value == "0" || value.Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }

        return false;
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

    private sealed record RecipientCsvSchema(
        IReadOnlyList<string> IdColumns,
        string DisplayNameColumn,
        string EmailColumn,
        string? ActiveColumn,
        IReadOnlyList<string> SearchColumns);
}
