using PrintRxerV3.Recipients;

namespace PrintRxerV3.Tests;

public sealed class RecipientCsvLoaderTests
{
    [Test]
    public void Load_skips_inactive_and_incomplete_rows()
    {
        string csvPath = WriteCsv(
            "RecipientName,EmailAddress,Active,MatchTerms",
            "Alpha Pharmacy,alpha@example.ie,TRUE,alpha;chemist",
            "Inactive Pharmacy,inactive@example.ie,NO,inactive",
            "Missing Email,,TRUE,missing",
            ",noname@example.ie,TRUE,noname");

        IReadOnlyList<RecipientRecord> recipients = RecipientCsvLoader.Load(csvPath);

        RecipientRecord recipient = Assert.Single(recipients);
        Assert.Equal("Alpha Pharmacy", recipient.RecipientName);
        Assert.Equal("alpha@example.ie", recipient.EmailAddress);
    }

    [Test]
    public void Load_supports_common_healthmail_header_names_and_alias_search_text()
    {
        string csvPath = WriteCsv(
            "Display Name,Healthmail Email,Aliases,County",
            "\"Beta Clinic\",beta@example.ie,\"beta;urgent\",Dublin");

        RecipientRecord recipient = Assert.Single(RecipientCsvLoader.Load(csvPath));

        Assert.Contains("beta", recipient.Aliases, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("urgent", recipient.Aliases, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("dublin", recipient.SearchText);
    }

    [Test]
    public void Load_parses_quoted_commas()
    {
        string csvPath = WriteCsv(
            "Name,Email,Match Terms",
            "\"Clinic, West\",west@example.ie,\"west;clinic\"");

        RecipientRecord recipient = Assert.Single(RecipientCsvLoader.Load(csvPath));

        Assert.Equal("Clinic, West", recipient.RecipientName);
        Assert.Equal("west@example.ie", recipient.EmailAddress);
    }

    private static string WriteCsv(params string[] lines)
    {
        string path = Path.Combine(Path.GetTempPath(), "printrxer-v3-" + Guid.NewGuid().ToString("N") + ".csv");
        File.WriteAllLines(path, lines);
        return path;
    }
}
