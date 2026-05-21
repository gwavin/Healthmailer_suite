using PrintRxerV3.Recipients;

namespace PrintRxerV3.App;

public static class RecipientSource
{
    public static IReadOnlyList<RecipientRecord> LoadDefault()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "printrxer_v3", "data", "recipients");
        if (Directory.Exists(folder))
        {
            string canonicalCsv = Path.Combine(folder, "recipients.csv");
            string? file = File.Exists(canonicalCsv)
                ? canonicalCsv
                : Directory.EnumerateFiles(folder, "*.*")
                    .Where(path => IsSupported(path))
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
            if (file is not null)
            {
                return RecipientCsvLoader.LoadAny(file);
            }
        }

        return new[]
        {
            new RecipientRecord
            {
                RecipientName = "Sample HealthMailer Recipient",
                EmailAddress = "sample.recipient@example.invalid",
                Aliases = Array.Empty<string>(),
                SearchTerms = new[] { "Sample HealthMailer Recipient", "sample.recipient@example.invalid" },
                SearchText = "sample healthmailer recipient sample.recipient@example.invalid"
            }
        };
    }

    private static bool IsSupported(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase);
    }
}
