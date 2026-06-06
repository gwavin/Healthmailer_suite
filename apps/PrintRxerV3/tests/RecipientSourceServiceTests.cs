using System.Text.Json;
using PrintRxerV3.Recipients;

namespace PrintRxerV3.Tests;

public sealed class RecipientSourceServiceTests
{
    [Test]
    public void Derives_central_recipient_path_from_handoff_root()
    {
        RecipientSourceOptions options = new()
        {
            HandoffRoot = @"\\server\HealthMailerDrop$\incoming"
        };

        Assert.Equal(@"\\server\HealthMailerDrop$\incoming\recipients", options.CentralRecipientFolder);
        Assert.Equal(@"\\server\HealthMailerDrop$\incoming\recipients\recipients.csv", options.CentralRecipientFile);
    }

    [Test]
    public void Rejects_path_traversal_in_central_relative_path()
    {
        RecipientSourceOptions options = new()
        {
            HandoffRoot = @"C:\handoff",
            CentralRelativePath = @"..\elsewhere"
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Test]
    public void Manual_refresh_loads_valid_central_recipients_and_updates_cache_status()
    {
        string root = NewTempRoot();
        string handoff = Path.Combine(root, "handoff");
        string local = Path.Combine(root, "local");
        string central = Path.Combine(handoff, "recipients", "recipients.csv");
        WriteCentralCsv(central, "central-1", "Central Clinic", "central@example.ie");
        WriteBundledCsv(Path.Combine(local, "bundled-recipients.csv"), "bundled-1", "Bundled Clinic", "bundled@example.ie");

        RecipientService service = new(new RecipientSourceOptions
        {
            HandoffRoot = handoff,
            LocalRecipientRoot = local
        });

        RecipientRefreshResult result = service.RefreshFromCentral();

        Assert.True(result.Success);
        Assert.Equal(RecipientSourceKind.Central, service.Current.SourceUsed);
        Assert.Equal("Central Clinic", Assert.Single(service.Current.Recipients).RecipientName);
        Assert.True(File.Exists(Path.Combine(local, "recipients.cache.csv")));
        RecipientSourceStatus status = ReadStatus(local);
        Assert.Equal(RecipientSourceKind.Central, status.SourceUsed);
        Assert.True(status.CentralAvailable);
        Assert.True(status.CentralValid);
        Assert.Equal(1, status.ActiveRecipientCount);
    }

    [Test]
    public void Loads_cache_when_central_unavailable()
    {
        string root = NewTempRoot();
        string local = Path.Combine(root, "local");
        WriteCentralCsv(Path.Combine(local, "recipients.cache.csv"), "cache-1", "Cache Clinic", "cache@example.ie");
        WriteBundledCsv(Path.Combine(local, "bundled-recipients.csv"), "bundled-1", "Bundled Clinic", "bundled@example.ie");

        RecipientService service = new(new RecipientSourceOptions
        {
            HandoffRoot = Path.Combine(root, "missing-handoff"),
            LocalRecipientRoot = local
        });

        RecipientSnapshot snapshot = service.LoadLocalFirst();

        Assert.Equal(RecipientSourceKind.Cache, snapshot.SourceUsed);
        Assert.Equal("Cache Clinic", Assert.Single(snapshot.Recipients).RecipientName);
    }

    [Test]
    public void Loads_stale_cache_with_warning_before_block_threshold()
    {
        string root = NewTempRoot();
        string local = Path.Combine(root, "local");
        string cache = Path.Combine(local, "recipients.cache.csv");
        WriteCentralCsv(cache, "cache-1", "Cache Clinic", "cache@example.ie");
        File.SetLastWriteTimeUtc(cache, DateTime.UtcNow.AddDays(-45));

        RecipientService service = new(new RecipientSourceOptions
        {
            HandoffRoot = Path.Combine(root, "missing-handoff"),
            LocalRecipientRoot = local,
            MaxCacheAgeDaysWarning = 30,
            MaxCacheAgeDaysBlock = 365
        });

        RecipientSnapshot snapshot = service.LoadLocalFirst();

        Assert.Equal(RecipientSourceKind.Cache, snapshot.SourceUsed);
        Assert.Contains("stale", snapshot.Warning, StringComparison.OrdinalIgnoreCase);
        RecipientSourceStatus status = ReadStatus(local);
        Assert.True(status.CacheAgeDays >= 44);
        Assert.Equal("Warning", status.CacheAgeStatus);
    }

    [Test]
    public void Blocks_cache_older_than_block_threshold_and_uses_bundled_fallback()
    {
        string root = NewTempRoot();
        string local = Path.Combine(root, "local");
        string cache = Path.Combine(local, "recipients.cache.csv");
        WriteCentralCsv(cache, "cache-1", "Cache Clinic", "cache@example.ie");
        WriteBundledCsv(Path.Combine(local, "bundled-recipients.csv"), "bundled-1", "Bundled Clinic", "bundled@example.ie");
        File.SetLastWriteTimeUtc(cache, DateTime.UtcNow.AddDays(-400));

        RecipientService service = new(new RecipientSourceOptions
        {
            HandoffRoot = Path.Combine(root, "missing-handoff"),
            LocalRecipientRoot = local,
            MaxCacheAgeDaysWarning = 30,
            MaxCacheAgeDaysBlock = 365
        });

        RecipientSnapshot snapshot = service.LoadLocalFirst();

        Assert.Equal(RecipientSourceKind.BundledFallback, snapshot.SourceUsed);
        Assert.Contains("Bundled Clinic", Assert.Single(snapshot.Recipients).RecipientName);
        Assert.Contains("cache blocked", snapshot.Warning, StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public void Loads_bundled_fallback_when_central_and_cache_unavailable()
    {
        string root = NewTempRoot();
        string local = Path.Combine(root, "local");
        WriteBundledCsv(Path.Combine(local, "bundled-recipients.csv"), "bundled-1", "Bundled Clinic", "bundled@example.ie");

        RecipientService service = new(new RecipientSourceOptions
        {
            HandoffRoot = Path.Combine(root, "missing-handoff"),
            LocalRecipientRoot = local
        });

        RecipientSnapshot snapshot = service.LoadLocalFirst();

        Assert.Equal(RecipientSourceKind.BundledFallback, snapshot.SourceUsed);
        Assert.Equal("Bundled Clinic", Assert.Single(snapshot.Recipients).RecipientName);
    }

    [Test]
    public void Invalid_central_file_does_not_overwrite_existing_cache()
    {
        string root = NewTempRoot();
        string handoff = Path.Combine(root, "handoff");
        string local = Path.Combine(root, "local");
        string cache = Path.Combine(local, "recipients.cache.csv");
        WriteCentralCsv(cache, "cache-1", "Cache Clinic", "cache@example.ie");
        WriteBundledCsv(Path.Combine(local, "bundled-recipients.csv"), "bundled-1", "Bundled Clinic", "bundled@example.ie");
        WriteText(Path.Combine(handoff, "recipients", "recipients.csv"), "recipientId,displayName,email,active\nbad,Bad,,true\n");

        RecipientService service = new(new RecipientSourceOptions
        {
            HandoffRoot = handoff,
            LocalRecipientRoot = local
        });

        RecipientRefreshResult result = service.RefreshFromCentral();

        Assert.False(result.Success);
        Assert.Contains("email", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cache Clinic", File.ReadAllText(cache));
        Assert.DoesNotContain("Bad", File.ReadAllText(cache));
    }

    [Test]
    public void Validator_rejects_duplicate_recipient_id()
    {
        string csv = Path.Combine(NewTempRoot(), "recipients.csv");
        WriteText(csv, "recipientId,displayName,email,active\nsame,A,a@example.ie,true\nsame,B,b@example.ie,true\n");

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => RecipientCsvValidator.LoadValidated(csv));

        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public void Validator_rejects_no_active_recipients()
    {
        string csv = Path.Combine(NewTempRoot(), "recipients.csv");
        WriteText(csv, "recipientId,displayName,email,active\none,A,a@example.ie,false\n");

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => RecipientCsvValidator.LoadValidated(csv));

        Assert.Contains("active", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public void Validator_accepts_healthmail_master_export_headers()
    {
        string csv = Path.Combine(NewTempRoot(), "recipients.csv");
        WriteText(csv, "DisplayName,Healthmail Address,Company,City,Phone,County ,Title\n" +
            "Central Healthmail,central.healthmail@healthmail.ie,Central Clinic,Dublin,01 555 0100,Dublin,Consultant\n");

        RecipientRecord recipient = Assert.Single(RecipientCsvValidator.LoadValidated(csv));

        Assert.Equal("Central Healthmail", recipient.RecipientName);
        Assert.Equal("central.healthmail@healthmail.ie", recipient.EmailAddress);
        Assert.Contains("Central Clinic", recipient.SearchTerms);
        Assert.Contains("Dublin", recipient.SearchTerms);
    }

    [Test]
    public void Validator_rejects_duplicate_healthmail_master_addresses()
    {
        string csv = Path.Combine(NewTempRoot(), "recipients.csv");
        WriteText(csv, "DisplayName,Healthmail Address,Company,City,Phone,County ,Title\n" +
            "Central A,central.healthmail@healthmail.ie,Central Clinic,Dublin,01 555 0100,Dublin,Consultant\n" +
            "Central B,central.healthmail@healthmail.ie,Central Clinic,Dublin,01 555 0101,Dublin,Consultant\n");

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => RecipientCsvValidator.LoadValidated(csv));

        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public void Refresh_tasks_do_not_overlap()
    {
        string root = NewTempRoot();
        string handoff = Path.Combine(root, "handoff");
        string local = Path.Combine(root, "local");
        WriteCentralCsv(Path.Combine(handoff, "recipients", "recipients.csv"), "central-1", "Central Clinic", "central@example.ie");

        RecipientService service = new(new RecipientSourceOptions
        {
            HandoffRoot = handoff,
            LocalRecipientRoot = local
        });

        Assert.True(service.TryBeginRefreshForTest());
        RecipientRefreshResult result = service.RefreshFromCentral();
        service.EndRefreshForTest();

        Assert.False(result.Success);
        Assert.Contains("already running", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static RecipientSourceStatus ReadStatus(string localRoot)
    {
        string json = File.ReadAllText(Path.Combine(localRoot, "recipient-source-status.json"));
        return JsonSerializer.Deserialize<RecipientSourceStatus>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private static string NewTempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-recipient-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void WriteCentralCsv(string path, string id, string name, string email)
    {
        WriteText(path, "recipientId,displayName,email,active,organisation,site,department,service,sortOrder,notes\n" +
            $"{id},{name},{email},true,,,,,,\n");
    }

    private static void WriteBundledCsv(string path, string id, string name, string email)
    {
        WriteCentralCsv(path, id, name, email);
    }

    private static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
