using System.Text.Json;
using Xunit;

namespace HealthMailer.Tests;

[Collection(ProcessHandleMeasurementCollection.Name)]
public sealed class PackageProcessorTests
{
    [Fact]
    public void EnsureDirectories_repeated_calls_do_not_leak_process_handles()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), "healthmailer-handles-" + Guid.NewGuid().ToString("N"));
        HealthMailerConfig config = new()
        {
            HandoffRoot = Path.Combine(root, "handoff"),
            LocalRoot = Path.Combine(root, "local"),
            SendMail = false
        };

        config.EnsureDirectories();
        int before = System.Diagnostics.Process.GetCurrentProcess().HandleCount;

        for (int index = 0; index < 20; index++)
        {
            config.EnsureDirectories();
        }

        int after = System.Diagnostics.Process.GetCurrentProcess().HandleCount;
        Assert.True(after - before <= 5, $"Handle count grew by {after - before}.");
    }

    [Fact]
    public void ProcessAvailablePackages_returns_zero_when_handoff_root_is_unavailable()
    {
        string root = Path.Combine(Path.GetTempPath(), "healthmailer-unavailable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string blockedHandoff = Path.Combine(root, "handoff");
        File.WriteAllText(blockedHandoff, "not a directory");
        List<string> logs = new();
        HealthMailerConfig config = new()
        {
            HandoffRoot = blockedHandoff,
            LocalRoot = Path.Combine(root, "local"),
            SendMail = false
        };

        PackageProcessor processor = new(config, new RecordingMailer(), logs.Add);

        Assert.Equal(0, processor.ProcessAvailablePackages());
        Assert.Contains(logs, line => line.Contains("handoff folder", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProcessAvailablePackages_valid_package_sends_copies_after_mail_writes_result_and_archives()
    {
        string handoffRoot = Path.Combine(Path.GetTempPath(), "healthmailer-handoff-" + Guid.NewGuid().ToString("N"));
        string localRoot = Path.Combine(Path.GetTempPath(), "healthmailer-local-" + Guid.NewGuid().ToString("N"));
        string chartRoot = Path.Combine(Path.GetTempPath(), "healthmailer-viewpoint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(handoffRoot);
        string packageDirectory = CreatePackage(handoffRoot, "pkg-1");
        RecordingMailer mailer = new();
        HealthMailerConfig config = new()
        {
            HandoffRoot = handoffRoot,
            LocalRoot = localRoot,
            SendMail = true,
            LiveSendingApproved = true,
            ChartCopy = new ChartCopyOptions
            {
                Enabled = true,
                DestinationRoot = chartRoot
            }
        };

        PackageProcessor processor = new(config, mailer, _ => { });
        int processed = processor.ProcessAvailablePackages();

        Assert.Equal(1, processed);
        Assert.Single(mailer.Sent);
        Assert.False(Directory.Exists(packageDirectory));
        Assert.True(Directory.Exists(Path.Combine(localRoot, "sent", "pkg-1")));
        Assert.Single(Directory.GetFiles(chartRoot, "*.pdf"));
        Assert.True(File.Exists(Path.Combine(localRoot, "sent", "pkg-1", "result.json")));
        Assert.Contains("Status: Sent", File.ReadAllText(Path.Combine(localRoot, "sent", "pkg-1", "summary.txt")));
    }

    [Fact]
    public void ProcessAvailablePackages_sendmail_false_validates_without_sent_outcome_or_duplicate_poisoning()
    {
        TestPaths paths = CreatePaths();
        CreatePackage(paths.HandoffRoot, "pkg-dry-run");
        RecordingMailer mailer = new();
        HealthMailerConfig config = CreateConfig(paths);
        config.SendMail = false;

        int processed = new PackageProcessor(config, mailer, _ => { }).ProcessAvailablePackages();

        Assert.Equal(1, processed);
        Assert.Empty(mailer.Sent);
        string archived = Path.Combine(paths.LocalRoot, "validated-no-send", "pkg-dry-run");
        Assert.True(Directory.Exists(archived));
        string resultJson = File.ReadAllText(Path.Combine(archived, "result.json"));
        Assert.Contains("ValidatedNoSend", resultJson);
        Assert.Contains("\"MailSent\": false", resultJson);
        Assert.Contains("Mail sent: False", File.ReadAllText(Path.Combine(archived, "summary.txt")));

        CreatePackage(paths.HandoffRoot, "pkg-dry-run");
        RecordingMailer liveMailer = new();
        HealthMailerConfig liveConfig = CreateConfig(paths);
        liveConfig.SendMail = true;
        liveConfig.LiveSendingApproved = true;

        int liveProcessed = new PackageProcessor(liveConfig, liveMailer, _ => { }).ProcessAvailablePackages();

        Assert.Equal(1, liveProcessed);
        Assert.Single(liveMailer.Sent);
    }

    [Fact]
    public void ProcessAvailablePackages_archives_when_handoff_and_local_roots_differ()
    {
        string rootA = Path.Combine(Path.GetTempPath(), "healthmailer-handoff-a-" + Guid.NewGuid().ToString("N"));
        string rootB = Path.Combine(Path.GetTempPath(), "healthmailer-local-b-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootA);
        string packageDirectory = CreatePackage(rootA, "pkg-cross-root");
        RecordingMailer mailer = new();
        HealthMailerConfig config = new()
        {
            HandoffRoot = rootA,
            LocalRoot = rootB,
            SendMail = false
        };

        PackageProcessor processor = new(config, mailer, _ => { });
        int processed = processor.ProcessAvailablePackages();

        Assert.Equal(1, processed);
        Assert.False(Directory.Exists(packageDirectory));
        Assert.True(File.Exists(Path.Combine(rootB, "validated-no-send", "pkg-cross-root", "result.json")));
        Assert.True(File.Exists(Path.Combine(rootB, "validated-no-send", "pkg-cross-root", "prescription.pdf")));
    }

    [Fact]
    public void ProcessAvailablePackages_missing_ready_is_ignored()
    {
        TestPaths paths = CreatePaths();
        string packageDirectory = CreatePackage(paths.HandoffRoot, "pkg-no-ready", includeReady: false);
        RecordingMailer mailer = new();

        int processed = CreateProcessor(paths, mailer).ProcessAvailablePackages();

        Assert.Equal(0, processed);
        Assert.Empty(mailer.Sent);
        Assert.True(Directory.Exists(packageDirectory));
    }

    [Fact]
    public void ProcessAvailablePackages_uploading_package_is_ignored_even_with_ready()
    {
        TestPaths paths = CreatePaths();
        string packageDirectory = CreatePackage(paths.HandoffRoot, ".uploading-pkg-1-test", includeReady: true);
        RecordingMailer mailer = new();

        int processed = CreateProcessor(paths, mailer).ProcessAvailablePackages();

        Assert.Equal(0, processed);
        Assert.Empty(mailer.Sent);
        Assert.True(Directory.Exists(packageDirectory));
    }

    [Fact]
    public void ProcessAvailablePackages_bad_pdf_hash_validation_fails_and_quarantines()
    {
        TestPaths paths = CreatePaths();
        CreatePackage(paths.HandoffRoot, "pkg-bad-hash", corruptRequestHash: true);
        RecordingMailer mailer = new();

        int processed = CreateProcessor(paths, mailer).ProcessAvailablePackages();

        Assert.Equal(1, processed);
        Assert.Empty(mailer.Sent);
        string quarantine = Path.Combine(paths.LocalRoot, "quarantine", "pkg-bad-hash");
        Assert.True(Directory.Exists(quarantine));
        Assert.Contains("ValidationFailed", File.ReadAllText(Path.Combine(quarantine, "result.json")));
    }

    [Fact]
    public void ProcessAvailablePackages_duplicate_package_is_quarantined_and_not_resent()
    {
        TestPaths paths = CreatePaths();
        CreatePackage(paths.HandoffRoot, "pkg-duplicate");
        RecordingMailer mailer = new();
        PackageProcessor processor = CreateProcessor(paths, mailer);
        processor.ProcessAvailablePackages();
        CreatePackage(paths.HandoffRoot, "pkg-duplicate");

        int processed = processor.ProcessAvailablePackages();

        Assert.Equal(1, processed);
        Assert.Single(mailer.Sent);
        Assert.True(Directory.Exists(Path.Combine(paths.LocalRoot, "quarantine", "pkg-duplicate")));
    }

    [Fact]
    public void ProcessAvailablePackages_mail_failure_does_not_copy_to_chart()
    {
        TestPaths paths = CreatePaths(chartEnabled: true);
        CreatePackage(paths.HandoffRoot, "pkg-mail-fail");
        RecordingMailer mailer = new() { Failure = new InvalidOperationException("mail offline") };

        int processed = CreateProcessor(paths, mailer).ProcessAvailablePackages();

        Assert.Equal(1, processed);
        Assert.Empty(Directory.GetFiles(paths.ChartRoot, "*.pdf"));
        string failed = Path.Combine(paths.LocalRoot, "failed", "pkg-mail-fail");
        Assert.True(Directory.Exists(failed));
        Assert.Contains("MailFailed", File.ReadAllText(Path.Combine(failed, "result.json")));
    }

    [Fact]
    public void ProcessAvailablePackages_chart_copy_failure_after_mail_is_recorded_distinctly()
    {
        TestPaths paths = CreatePaths(chartEnabled: true);
        CreatePackage(paths.HandoffRoot, "pkg-chart-fail");
        RecordingMailer mailer = new();
        ThrowingChartCopy chartCopy = new();

        int processed = new PackageProcessor(CreateConfig(paths), mailer, chartCopy, _ => { }).ProcessAvailablePackages();

        Assert.Equal(1, processed);
        Assert.Single(mailer.Sent);
        string failed = Path.Combine(paths.LocalRoot, "failed", "pkg-chart-fail");
        Assert.True(Directory.Exists(failed));
        Assert.Contains("ChartCopyFailed", File.ReadAllText(Path.Combine(failed, "result.json")));
        Assert.Contains("\"MailSent\": true", File.ReadAllText(Path.Combine(failed, "result.json")));
    }

    [Fact]
    public void ProcessAvailablePackages_chart_copy_failure_after_mail_duplicate_protects_reintroduced_package()
    {
        TestPaths paths = CreatePaths(chartEnabled: true);
        CreatePackage(paths.HandoffRoot, "pkg-chart-duplicate");
        RecordingMailer mailer = new();
        ThrowingChartCopy chartCopy = new();
        HealthMailerConfig config = CreateConfig(paths);
        PackageProcessor processor = new(config, mailer, chartCopy, _ => { });
        processor.ProcessAvailablePackages();
        CreatePackage(paths.HandoffRoot, "pkg-chart-duplicate");

        int processed = processor.ProcessAvailablePackages();

        Assert.Equal(1, processed);
        Assert.Single(mailer.Sent);
        Assert.True(Directory.Exists(Path.Combine(paths.LocalRoot, "quarantine", "pkg-chart-duplicate")));
    }

    [Theory]
    [InlineData("recipient@healthmail.ie")]
    [InlineData("recipient@hse.ie")]
    [InlineData("recipient@nmh.ie")]
    [InlineData("recipient@rotunda.ie")]
    [InlineData("RECIPIENT@HEALTHMAIL.IE")]
    public void ProcessAvailablePackages_allows_approved_recipient_domains_at_send_boundary(string email)
    {
        TestPaths paths = CreatePaths();
        CreatePackage(paths.HandoffRoot, "pkg-domain-ok", recipientEmail: email);
        RecordingMailer mailer = new();

        int processed = CreateProcessor(paths, mailer).ProcessAvailablePackages();

        Assert.Equal(1, processed);
        Assert.Single(mailer.Sent);
    }

    [Theory]
    [InlineData("recipient@gmail.com")]
    [InlineData("recipient@example.com")]
    [InlineData("")]
    [InlineData("not-an-address")]
    public void ProcessAvailablePackages_rejects_unapproved_or_malformed_recipient_at_send_boundary(string email)
    {
        TestPaths paths = CreatePaths();
        CreatePackage(paths.HandoffRoot, "pkg-domain-bad", recipientEmail: email);
        RecordingMailer mailer = new();

        int processed = CreateProcessor(paths, mailer).ProcessAvailablePackages();

        Assert.Equal(1, processed);
        Assert.Empty(mailer.Sent);
        string quarantine = Path.Combine(paths.LocalRoot, "quarantine", "pkg-domain-bad");
        Assert.True(Directory.Exists(quarantine));
        Assert.Contains("RecipientRejected", File.ReadAllText(Path.Combine(quarantine, "result.json")));
    }

    [Fact]
    public void ProcessAvailablePackages_stale_lock_is_retried()
    {
        TestPaths paths = CreatePaths();
        string packageDirectory = CreatePackage(paths.HandoffRoot, "pkg-stale-lock");
        File.WriteAllText(Path.Combine(packageDirectory, ".healthmailer.lock"), DateTimeOffset.UtcNow.AddHours(-2).ToString("O"));
        RecordingMailer mailer = new();

        int processed = CreateProcessor(paths, mailer).ProcessAvailablePackages();

        Assert.Equal(1, processed);
        Assert.Single(mailer.Sent);
    }

    [Fact]
    public void ProcessAvailablePackages_fresh_lock_is_ignored()
    {
        TestPaths paths = CreatePaths();
        string packageDirectory = CreatePackage(paths.HandoffRoot, "pkg-fresh-lock");
        File.WriteAllText(Path.Combine(packageDirectory, ".healthmailer.lock"), DateTimeOffset.UtcNow.ToString("O"));
        RecordingMailer mailer = new();

        int processed = CreateProcessor(paths, mailer).ProcessAvailablePackages();

        Assert.Equal(0, processed);
        Assert.Empty(mailer.Sent);
        Assert.True(Directory.Exists(packageDirectory));
    }

    [Fact]
    public void ProcessAvailablePackages_invalid_stale_lock_uses_file_timestamp_and_retries()
    {
        TestPaths paths = CreatePaths();
        string packageDirectory = CreatePackage(paths.HandoffRoot, "pkg-invalid-stale-lock");
        string lockPath = Path.Combine(packageDirectory, ".healthmailer.lock");
        File.WriteAllText(lockPath, "not a timestamp");
        File.SetLastWriteTimeUtc(lockPath, DateTime.UtcNow.AddHours(-2));
        RecordingMailer mailer = new();

        int processed = CreateProcessor(paths, mailer).ProcessAvailablePackages();

        Assert.Equal(1, processed);
        Assert.Single(mailer.Sent);
    }

    [Fact]
    public async Task TryProcessPackage_concurrent_claim_allows_only_one_processor()
    {
        TestPaths paths = CreatePaths();
        string packageDirectory = CreatePackage(paths.HandoffRoot, "pkg-concurrent-lock");
        BlockingMailer mailer = new();
        PackageProcessor first = CreateProcessor(paths, mailer);
        PackageProcessor second = CreateProcessor(paths, new RecordingMailer());

        Task<bool> firstAttempt = Task.Run(() => first.TryProcessPackage(packageDirectory));
        Assert.True(mailer.Started.Wait(TimeSpan.FromSeconds(5)));

        bool secondAttempt = second.TryProcessPackage(packageDirectory);
        mailer.Release.Set();

        Assert.True(await firstAttempt);
        Assert.False(secondAttempt);
        Assert.Single(mailer.Sent);
    }

    [Fact]
    public void ProcessAvailablePackages_does_not_delete_archives_during_normal_processing()
    {
        TestPaths paths = CreatePaths();
        string oldSent = Path.Combine(paths.LocalRoot, "sent", "old-sent");
        string oldFailed = Path.Combine(paths.LocalRoot, "failed", "old-failed");
        string oldQuarantine = Path.Combine(paths.LocalRoot, "quarantine", "old-quarantine");
        Directory.CreateDirectory(oldSent);
        Directory.CreateDirectory(oldFailed);
        Directory.CreateDirectory(oldQuarantine);
        Directory.SetLastWriteTimeUtc(oldSent, DateTime.UtcNow.AddDays(-120));
        Directory.SetLastWriteTimeUtc(oldFailed, DateTime.UtcNow.AddDays(-120));
        Directory.SetLastWriteTimeUtc(oldQuarantine, DateTime.UtcNow.AddDays(-120));
        CreatePackage(paths.HandoffRoot, "pkg-archive-retention");

        int processed = CreateProcessor(paths, new RecordingMailer()).ProcessAvailablePackages();

        Assert.Equal(1, processed);
        Assert.True(Directory.Exists(oldSent));
        Assert.True(Directory.Exists(oldFailed));
        Assert.True(Directory.Exists(oldQuarantine));
    }

    [Fact]
    public void SummaryHtml_contains_no_scripts_or_external_resources()
    {
        TestPaths paths = CreatePaths();
        HealthMailerConfig config = CreateConfig(paths);
        config.WriteHtmlSummary = true;
        CreatePackage(paths.HandoffRoot, "pkg-html-summary");

        new PackageProcessor(config, new RecordingMailer(), _ => { }).ProcessAvailablePackages();

        string html = File.ReadAllText(Path.Combine(paths.LocalRoot, "sent", "pkg-html-summary", "summary.html"));
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreatePackage(string root, string packageId, bool includeReady = true, bool corruptRequestHash = false, string recipientEmail = "recipient@healthmail.ie")
    {
        string packageDirectory = Path.Combine(root, packageId);
        Directory.CreateDirectory(packageDirectory);
        string pdfPath = Path.Combine(packageDirectory, "prescription.pdf");
        File.WriteAllText(pdfPath, "%PDF-1.4\n% test\n");
        string hash = SecurityUtilities.ComputeSha256(pdfPath);
        File.WriteAllText(Path.Combine(packageDirectory, "request.json"), JsonSerializer.Serialize(new
        {
            packageId,
            selectedRecipientEmail = recipientEmail,
            selectedRecipientName = "Recipient",
            subject = "Prescription",
            body = "Please see attached.",
            pdfSha256 = corruptRequestHash ? "bad" : hash,
            mrn = "MRN999"
        }));
        File.WriteAllText(Path.Combine(packageDirectory, "request.sha256"), hash + "  prescription.pdf");
        File.WriteAllText(Path.Combine(packageDirectory, "summary.txt"), "printRxer handoff package");
        if (includeReady)
        {
            File.WriteAllText(Path.Combine(packageDirectory, "READY"), string.Empty);
        }

        return packageDirectory;
    }

    private static TestPaths CreatePaths(bool chartEnabled = true)
    {
        string root = Path.Combine(Path.GetTempPath(), "healthmailer-processor-" + Guid.NewGuid().ToString("N"));
        TestPaths paths = new(
            Path.Combine(root, "handoff"),
            Path.Combine(root, "local"),
            Path.Combine(root, "chart"));
        Directory.CreateDirectory(paths.HandoffRoot);
        Directory.CreateDirectory(paths.ChartRoot);
        return paths;
    }

    private static HealthMailerConfig CreateConfig(TestPaths paths)
    {
        return new HealthMailerConfig
        {
            HandoffRoot = paths.HandoffRoot,
            LocalRoot = paths.LocalRoot,
            SendMail = true,
            ConfigCreatedByInstaller = true,
            LiveSendingApproved = true,
            ChartCopy = new ChartCopyOptions
            {
                Enabled = !string.IsNullOrWhiteSpace(paths.ChartRoot),
                DestinationRoot = paths.ChartRoot
            }
        };
    }

    private static PackageProcessor CreateProcessor(TestPaths paths, IMailHandoff mailer)
    {
        return new PackageProcessor(CreateConfig(paths), mailer, _ => { });
    }

    private sealed record TestPaths(string HandoffRoot, string LocalRoot, string ChartRoot);

    private sealed class RecordingMailer : IMailHandoff
    {
        public List<DeliveryPackage> Sent { get; } = [];
        public Exception? Failure { get; init; }

        public void Send(DeliveryPackage package)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Sent.Add(package);
        }
    }

    private sealed class BlockingMailer : IMailHandoff
    {
        public ManualResetEventSlim Started { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public List<DeliveryPackage> Sent { get; } = [];

        public void Send(DeliveryPackage package)
        {
            Sent.Add(package);
            Started.Set();
            Assert.True(Release.Wait(TimeSpan.FromSeconds(5)));
        }
    }

    private sealed class ThrowingChartCopy : IChartCopyWriter
    {
        public string CopyToChartFolder(DeliveryPackage package, ChartCopyOptions options)
        {
            throw new IOException("chart folder unavailable");
        }
    }
}
