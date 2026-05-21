using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HealthMailer;

public enum PackageOutcome
{
    Sent,
    Failed,
    Quarantined,
    Duplicate,
    ValidationFailed,
    ChartCopyFailed,
    MailFailed
}

public sealed record ProcessingResult
{
    public required string PackageId { get; init; }
    public required PackageOutcome Outcome { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
    public string Message { get; init; } = string.Empty;
    public string RecipientEmail { get; init; } = string.Empty;
    public string PdfSha256 { get; init; } = string.Empty;
    public string CompletedPackageHash { get; init; } = string.Empty;
    public bool MailSent { get; init; }
    public bool ChartCopied { get; init; }
    public string ChartCopyPath { get; init; } = string.Empty;
}

public sealed class ProcessingAuditWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly JavaScriptEncoder HtmlEncoder = JavaScriptEncoder.Default;

    public void WriteTerminalRecords(string packageDirectory, ProcessingResult result, HealthMailerConfig config)
    {
        File.WriteAllText(Path.Combine(packageDirectory, "result.json"), JsonSerializer.Serialize(result, JsonOptions));
        File.WriteAllText(Path.Combine(packageDirectory, "summary.txt"), BuildTextSummary(result));
        if (config.WriteHtmlSummary)
        {
            File.WriteAllText(Path.Combine(packageDirectory, "summary.html"), BuildHtmlSummary(result));
        }
    }

    public static string BuildTextSummary(ProcessingResult result)
    {
        return string.Join(Environment.NewLine, new[]
        {
            "HealthMailer processing summary",
            "Status: " + result.Outcome,
            "Package ID: " + result.PackageId,
            "Recipient: " + result.RecipientEmail,
            "Completed UTC: " + result.CompletedAtUtc.ToString("O"),
            "Mail sent: " + result.MailSent,
            "Chart copied: " + result.ChartCopied,
            "Message: " + result.Message
        }) + Environment.NewLine;
    }

    public static string BuildHtmlSummary(ProcessingResult result)
    {
        static string H(string value) => HtmlEncoder.Encode(value ?? string.Empty);
        return "<!doctype html><html><head><meta charset=\"utf-8\"><title>HealthMailer summary</title></head><body>" +
            "<h1>HealthMailer processing summary</h1>" +
            "<dl>" +
            "<dt>Status</dt><dd>" + H(result.Outcome.ToString()) + "</dd>" +
            "<dt>Package ID</dt><dd>" + H(result.PackageId) + "</dd>" +
            "<dt>Recipient</dt><dd>" + H(result.RecipientEmail) + "</dd>" +
            "<dt>Completed UTC</dt><dd>" + H(result.CompletedAtUtc.ToString("O")) + "</dd>" +
            "<dt>Mail sent</dt><dd>" + H(result.MailSent.ToString()) + "</dd>" +
            "<dt>Chart copied</dt><dd>" + H(result.ChartCopied.ToString()) + "</dd>" +
            "<dt>Message</dt><dd>" + H(result.Message) + "</dd>" +
            "</dl></body></html>";
    }
}

public sealed class ProcessedPackageLedger
{
    private readonly string _path;
    private readonly object _sync = new();
    private readonly HashSet<string> _sentPackageIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _sentCompletedHashes = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _loadedLastWriteTimeUtc = DateTime.MinValue;
    private long _loadedLength = -1;
    private bool _loaded;

    public ProcessedPackageLedger(string path)
    {
        _path = path;
    }

    public bool HasSent(DeliveryPackage package)
    {
        lock (_sync)
        {
            ReloadIfChanged();
            return _sentPackageIds.Contains(package.PackageId) ||
                (!string.IsNullOrWhiteSpace(package.CompletedPackageHash) && _sentCompletedHashes.Contains(package.CompletedPackageHash));
        }
    }

    public void Append(ProcessingResult result)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string line = JsonSerializer.Serialize(new
        {
            result.PackageId,
            outcome = result.Outcome.ToString(),
            result.CompletedAtUtc,
            result.RecipientEmail,
            result.PdfSha256,
            result.CompletedPackageHash
        }) + Environment.NewLine;

        lock (_sync)
        {
            using FileStream stream = new(_path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            stream.Seek(0, SeekOrigin.End);
            using StreamWriter writer = new(stream);
            writer.Write(line);
            writer.Flush();
            AddToCacheIfSent(result.Outcome.ToString(), result.PackageId, result.CompletedPackageHash);
            UpdateLoadedFileState();
        }

        SecurityUtilities.TryHardenLedgerFile(_path);
    }

    private void ReloadIfChanged()
    {
        FileInfo info = new(_path);
        if (!info.Exists)
        {
            _sentPackageIds.Clear();
            _sentCompletedHashes.Clear();
            _loaded = true;
            _loadedLastWriteTimeUtc = DateTime.MinValue;
            _loadedLength = -1;
            return;
        }

        if (_loaded && info.LastWriteTimeUtc == _loadedLastWriteTimeUtc && info.Length == _loadedLength)
        {
            return;
        }

        _sentPackageIds.Clear();
        _sentCompletedHashes.Clear();
        foreach (string line in File.ReadLines(_path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                string outcome = root.TryGetProperty("outcome", out JsonElement status) ? status.ToString() : string.Empty;
                AddToCacheIfSent(
                    outcome,
                    TryRead(root, "packageId", "PackageId"),
                    TryRead(root, "completedPackageHash", "CompletedPackageHash"));
            }
            catch (JsonException)
            {
                continue;
            }
        }

        UpdateLoadedFileState(info);
    }

    private void AddToCacheIfSent(string outcome, string packageId, string completedPackageHash)
    {
        if (!string.Equals(outcome, PackageOutcome.Sent.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(packageId))
        {
            _sentPackageIds.Add(packageId);
        }

        if (!string.IsNullOrWhiteSpace(completedPackageHash))
        {
            _sentCompletedHashes.Add(completedPackageHash);
        }
    }

    private void UpdateLoadedFileState(FileInfo? info = null)
    {
        info ??= new FileInfo(_path);
        info.Refresh();
        _loaded = true;
        _loadedLastWriteTimeUtc = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;
        _loadedLength = info.Exists ? info.Length : -1;
    }

    private static string TryRead(JsonElement root, params string[] names)
    {
        foreach (string name in names)
        {
            if (root.TryGetProperty(name, out JsonElement value))
            {
                return value.ToString();
            }
        }

        return string.Empty;
    }
}
