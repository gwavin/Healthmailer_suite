using PrintRxerV3.Capture;
using PrintRxerV3.Handoff;
using PrintRxerV3.Metadata;
using PrintRxerV3.Packaging;
using System.Text.Json;
using Xunit;

namespace PrintRxerV3.Tests;

public sealed class CapturedPrintJobProcessorTests
{
    [Fact]
    public void ProcessOne_moves_ready_capture_to_processed_after_handoff_package_is_written()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-capture-" + Guid.NewGuid().ToString("N"));
        string incoming = Path.Combine(root, "incoming");
        string processed = Path.Combine(root, "processed");
        string handoff = Path.Combine(root, "handoff");
        string job = Path.Combine(incoming, "20260509-120000000-job42");
        Directory.CreateDirectory(job);
        File.WriteAllText(Path.Combine(job, "job.xps"), "not a real xps, but a captured payload");
        File.WriteAllText(Path.Combine(job, "metadata.json"), """
            {
              "source": "PrintRxer.PortMonitor",
              "portName": "printrx:",
              "printerName": "printRxer",
              "documentName": "document",
              "jobId": 42,
              "SubmittingUser": "gavin",
              "SubmittingUserSid": "S-1",
              "capturedAtUtc": "2026-05-09T12:00:00.000Z",
              "payloadFile": "job.xps"
            }
            """);

        CapturedPrintJobProcessor processor = new(new CapturedPrintJobProcessorOptions
        {
            IncomingRoot = incoming,
            ProcessedRoot = processed,
            HandoffRoot = handoff,
            CurrentUserSidProvider = () => "S-1",
            PayloadStableSeconds = 0,
            PayloadStabilityProbeDelay = TimeSpan.Zero,
            PreparePdfFromCapture = WriteFakePdf,
            SelectRecipient = context => new PrintRxerV3.Metadata.PickerSelection
            {
                RecipientName = "Chosen Pharmacy",
                RecipientEmail = "chosen@example.ie",
                Subject = "Chosen subject",
                Body = "Chosen body",
                SelectedAt = DateTimeOffset.UtcNow
            }
        });

        CapturedPrintJobResult? result = processor.ProcessOne();

        Assert.NotNull(result);
        Assert.True(result.PackageCreated);
        Assert.Equal("PackagePublished", result.Outcome);
        Assert.NotNull(result.PackageDirectory);
        Assert.True(Directory.Exists(result.PackageDirectory));
        Assert.True(File.Exists(Path.Combine(result.PackageDirectory, "request.json")));
        Assert.True(File.Exists(Path.Combine(result.PackageDirectory, "READY")));
        string requestJson = File.ReadAllText(Path.Combine(result.PackageDirectory, "request.json"));
        Assert.Contains("chosen@example.ie", requestJson);
        using JsonDocument request = JsonDocument.Parse(requestJson);
        Assert.Contains("Intended recipient: Chosen Pharmacy <chosen@example.ie>", request.RootElement.GetProperty("body").GetString());
        Assert.False(Directory.Exists(job));
        Assert.True(Directory.Exists(Path.Combine(processed, "20260509-120000000-job42")));
    }

    [Fact]
    public void ProcessOne_cleans_completed_print_jobs_before_opening_picker()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-queue-clean-" + Guid.NewGuid().ToString("N"));
        string incoming = Path.Combine(root, "incoming");
        WriteCapture(incoming, "20260509-120000000-job99", submittingUserSid: "S-1");
        int cleanupCalls = 0;
        int pickerCalls = 0;

        CapturedPrintJobProcessor processor = new(new CapturedPrintJobProcessorOptions
        {
            IncomingRoot = incoming,
            ProcessedRoot = Path.Combine(root, "processed"),
            HandoffRoot = Path.Combine(root, "handoff"),
            CurrentUserSidProvider = () => "S-1",
            PayloadStableSeconds = 0,
            PayloadStabilityProbeDelay = TimeSpan.Zero,
            CleanupCompletedPrintJobs = () => cleanupCalls++,
            PreparePdfFromCapture = WriteFakePdf,
            SelectRecipient = _ =>
            {
                pickerCalls++;
                return null;
            }
        });

        CapturedPrintJobResult? result = processor.ProcessOne();

        Assert.NotNull(result);
        Assert.Equal("RecipientSelectionCancelled", result.Outcome);
        Assert.Equal(1, cleanupCalls);
        Assert.Equal(1, pickerCalls);
    }

    [Fact]
    public void ProcessOne_publishes_package_when_handoff_folder_is_available()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-publish-ok-" + Guid.NewGuid().ToString("N"));
        string incoming = Path.Combine(root, "incoming");
        string handoff = Path.Combine(root, "handoff");
        string outbox = Path.Combine(root, "pending-outbox");
        string published = Path.Combine(root, "published");
        List<string> logs = new();
        WriteCapture(incoming, "20260509-120000000-job60", submittingUserSid: "S-1");

        CapturedPrintJobProcessor processor = CreatePublishTestProcessor(root, incoming, handoff, outbox, published, logs.Add);

        CapturedPrintJobResult? result = processor.ProcessOne();

        Assert.NotNull(result);
        Assert.True(result.PackageCreated);
        Assert.Equal("PackagePublished", result.Outcome);
        Assert.NotNull(result.PackageDirectory);
        Assert.True(File.Exists(Path.Combine(result.PackageDirectory, "READY")));
        Assert.Empty(Directory.EnumerateDirectories(outbox));
        Assert.Single(Directory.EnumerateDirectories(published));
        Assert.Contains(logs, line => line.StartsWith("PackageQueuedLocal:", StringComparison.Ordinal));
        Assert.Contains(logs, line => line.StartsWith("PackagePublished:", StringComparison.Ordinal));
    }

    [Fact]
    public void ProcessOne_keeps_package_in_local_outbox_when_handoff_folder_is_unavailable()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-publish-defer-" + Guid.NewGuid().ToString("N"));
        string incoming = Path.Combine(root, "incoming");
        string handoffBlockedByFile = Path.Combine(root, "handoff");
        string outbox = Path.Combine(root, "pending-outbox");
        string published = Path.Combine(root, "published");
        List<string> logs = new();
        Directory.CreateDirectory(root);
        File.WriteAllText(handoffBlockedByFile, "not a directory");
        WriteCapture(incoming, "20260509-120000000-job61", submittingUserSid: "S-1");

        CapturedPrintJobProcessor processor = CreatePublishTestProcessor(root, incoming, handoffBlockedByFile, outbox, published, logs.Add);

        CapturedPrintJobResult? result = processor.ProcessOne();

        Assert.NotNull(result);
        Assert.True(result.PackageCreated);
        Assert.Equal("PackagePublishDeferred", result.Outcome);
        Assert.Null(result.PackageDirectory);
        Assert.NotNull(result.LocalPackageDirectory);
        Assert.Single(Directory.EnumerateDirectories(outbox));
        Assert.Empty(Directory.EnumerateDirectories(published));
        Assert.Contains(logs, line => line.StartsWith("PackageQueuedLocal:", StringComparison.Ordinal));
        Assert.Contains(logs, line => line.StartsWith("PackagePublishDeferred:", StringComparison.Ordinal));
        Assert.Contains(logs, line => line.Contains("Package queued locally; handoff folder unavailable; will retry automatically.", StringComparison.Ordinal));
    }

    [Fact]
    public void ProcessOne_moves_package_to_failed_when_publish_failure_is_not_recoverable()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-publish-failed-" + Guid.NewGuid().ToString("N"));
        string incoming = Path.Combine(root, "incoming");
        string handoff = Path.Combine(root, "handoff");
        string outbox = Path.Combine(root, "pending-outbox");
        string published = Path.Combine(root, "published");
        string failed = Path.Combine(root, "failed");
        List<string> logs = new();
        WriteCapture(incoming, "20260509-120000000-job63", submittingUserSid: "S-1");
        string mismatchedFinalPackage = Path.Combine(handoff, "fixed-package");
        Directory.CreateDirectory(mismatchedFinalPackage);
        File.WriteAllText(Path.Combine(mismatchedFinalPackage, "request.json"), "{}");
        File.WriteAllText(Path.Combine(mismatchedFinalPackage, "prescription.pdf"), "%PDF-1.4\n% different\n");
        File.WriteAllText(Path.Combine(mismatchedFinalPackage, "request.sha256"), "different  prescription.pdf");
        File.WriteAllText(Path.Combine(mismatchedFinalPackage, "summary.txt"), "different");
        File.WriteAllText(Path.Combine(mismatchedFinalPackage, "READY"), string.Empty);

        CapturedPrintJobProcessor processor = CreatePublishTestProcessor(
            root,
            incoming,
            handoff,
            outbox,
            published,
            logs.Add,
            packageIdProvider: () => "fixed-package",
            failedRoot: failed);

        CapturedPrintJobResult? result = processor.ProcessOne();

        Assert.NotNull(result);
        Assert.True(result.PackageCreated);
        Assert.Equal("PackagePublishFailed", result.Outcome);
        Assert.Null(result.PackageDirectory);
        Assert.NotNull(result.LocalPackageDirectory);
        Assert.Empty(Directory.EnumerateDirectories(outbox));
        Assert.Empty(Directory.EnumerateDirectories(published));
        string failedPackage = Path.Combine(failed, "fixed-package");
        Assert.True(Directory.Exists(failedPackage));
        Assert.True(File.Exists(Path.Combine(failedPackage, "printrxer_v3_failure.txt")));
        Assert.Contains("PackagePublishFailed", File.ReadAllText(Path.Combine(failedPackage, "printrxer_v3_failure.txt")));
        Assert.Contains(logs, line => line.StartsWith("PackagePublishFailed:", StringComparison.Ordinal));
    }

    [Fact]
    public void RetryPendingPublication_publishes_local_outbox_after_handoff_folder_returns()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-publish-retry-" + Guid.NewGuid().ToString("N"));
        string incoming = Path.Combine(root, "incoming");
        string handoff = Path.Combine(root, "handoff");
        string outbox = Path.Combine(root, "pending-outbox");
        string published = Path.Combine(root, "published");
        Directory.CreateDirectory(root);
        File.WriteAllText(handoff, "not a directory");
        WriteCapture(incoming, "20260509-120000000-job62", submittingUserSid: "S-1");

        CapturedPrintJobProcessor processor = CreatePublishTestProcessor(root, incoming, handoff, outbox, published);
        CapturedPrintJobResult? deferred = processor.ProcessOne();
        File.Delete(handoff);

        PrintRxerV3.Handoff.HandoffPublishResult retry = processor.RetryPendingPublication();

        Assert.NotNull(deferred);
        Assert.Equal("PackagePublishDeferred", deferred.Outcome);
        Assert.True(retry.Published);
        Assert.Equal("PackagePublished", retry.Outcome);
        Assert.Empty(Directory.EnumerateDirectories(outbox));
        Assert.Single(Directory.EnumerateDirectories(handoff));
        Assert.Single(Directory.EnumerateDirectories(published));
    }

    [Fact]
    public void RetryPendingPublication_is_idempotent_when_package_already_exists_in_handoff()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-publish-idem-" + Guid.NewGuid().ToString("N"));
        string handoff = Path.Combine(root, "handoff");
        string outbox = Path.Combine(root, "pending-outbox");
        string published = Path.Combine(root, "published");
        string localPackage = WritePendingPackage(outbox, "pkg-1");
        CopyDirectory(localPackage, Path.Combine(handoff, "pkg-1"));

        CapturedPrintJobProcessor processor = new(new CapturedPrintJobProcessorOptions
        {
            IncomingRoot = Path.Combine(root, "incoming"),
            ProcessedRoot = Path.Combine(root, "processed"),
            LocalOutboxRoot = outbox,
            PublishedRoot = published,
            HandoffRoot = handoff
        });

        PrintRxerV3.Handoff.HandoffPublishResult result = processor.RetryPendingPublication();

        Assert.True(result.Published);
        Assert.Empty(Directory.EnumerateDirectories(outbox));
        Assert.True(Directory.Exists(Path.Combine(published, "pkg-1")));
    }

    [Fact]
    public void ProcessOne_returns_null_when_no_capture_is_ready()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-empty-" + Guid.NewGuid().ToString("N"));

        CapturedPrintJobProcessor processor = new(new CapturedPrintJobProcessorOptions
        {
            IncomingRoot = Path.Combine(root, "incoming"),
            ProcessedRoot = Path.Combine(root, "processed"),
            HandoffRoot = Path.Combine(root, "handoff")
        });

        Assert.Null(processor.ProcessOne());
    }

    [Fact]
    public void ProcessOne_moves_capture_to_deferred_when_recipient_selection_is_cancelled()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-cancel-" + Guid.NewGuid().ToString("N"));
        string incoming = Path.Combine(root, "incoming");
        string processed = Path.Combine(root, "processed");
        string deferred = Path.Combine(root, "deferred");
        string handoff = Path.Combine(root, "handoff");
        string job = Path.Combine(incoming, "20260509-120000000-job43");
        Directory.CreateDirectory(job);
        File.WriteAllText(Path.Combine(job, "job.xps"), "payload");
        File.WriteAllText(Path.Combine(job, "metadata.json"), """
            {
              "source": "PrintRxer.PortMonitor",
              "printerName": "printRxer",
              "documentName": "document",
              "jobId": 43,
              "payloadFile": "job.xps"
            }
            """);

        CapturedPrintJobProcessor processor = new(new CapturedPrintJobProcessorOptions
        {
            IncomingRoot = incoming,
            ProcessedRoot = processed,
            DeferredRoot = deferred,
            HandoffRoot = handoff,
            AllowMissingJobOwnerForImport = true,
            PayloadStableSeconds = 0,
            PayloadStabilityProbeDelay = TimeSpan.Zero,
            SelectRecipient = _ => null
        });

        CapturedPrintJobResult? result = processor.ProcessOne();

        Assert.NotNull(result);
        Assert.False(result.PackageCreated);
        Assert.Equal("RecipientSelectionCancelled", result.Outcome);
        Assert.Null(result.PackageDirectory);
        Assert.False(Directory.Exists(job));
        Assert.True(Directory.Exists(Path.Combine(deferred, "20260509-120000000-job43")));
        Assert.False(Directory.Exists(handoff));
    }

    [Fact]
    public void ProcessOne_moves_capture_to_deferred_when_pdf_rendering_fails()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-render-fail-" + Guid.NewGuid().ToString("N"));
        string incoming = Path.Combine(root, "incoming");
        string processed = Path.Combine(root, "processed");
        string deferred = Path.Combine(root, "deferred");
        string handoff = Path.Combine(root, "handoff");
        string job = Path.Combine(incoming, "20260509-120000000-job44");
        Directory.CreateDirectory(job);
        File.WriteAllText(Path.Combine(job, "job.xps"), "payload");
        File.WriteAllText(Path.Combine(job, "metadata.json"), """
            {
              "source": "PrintRxer.PortMonitor",
              "printerName": "printRxer",
              "documentName": "document",
              "jobId": 44,
              "payloadFile": "job.xps"
            }
            """);

        CapturedPrintJobProcessor processor = new(new CapturedPrintJobProcessorOptions
        {
            IncomingRoot = incoming,
            ProcessedRoot = processed,
            DeferredRoot = deferred,
            HandoffRoot = handoff,
            AllowMissingJobOwnerForImport = true,
            PayloadStableSeconds = 0,
            PayloadStabilityProbeDelay = TimeSpan.Zero,
            PreparePdfFromCapture = (_, _) => throw new InvalidOperationException("render failed"),
            SelectRecipient = _ => new PrintRxerV3.Metadata.PickerSelection
            {
                RecipientName = "Chosen Pharmacy",
                RecipientEmail = "chosen@example.ie",
                Subject = "Chosen subject",
                Body = "Chosen body",
                SelectedAt = DateTimeOffset.UtcNow
            }
        });

        CapturedPrintJobResult? result = processor.ProcessOne();

        string deferredJob = Path.Combine(deferred, "20260509-120000000-job44");
        Assert.NotNull(result);
        Assert.False(result.PackageCreated);
        Assert.Equal("RenderFailed", result.Outcome);
        Assert.Null(result.PackageDirectory);
        Assert.False(Directory.Exists(job));
        Assert.True(Directory.Exists(deferredJob));
        Assert.True(File.Exists(Path.Combine(deferredJob, "printrxer_v3_failure.txt")));
        Assert.False(Directory.Exists(handoff));
    }

    [Fact]
    public void ProcessOne_creates_package_when_submitting_sid_matches_current_user()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-owner-match-" + Guid.NewGuid().ToString("N"));
        string incoming = Path.Combine(root, "incoming");
        string job = WriteCapture(incoming, "20260509-120000000-job45", submittingUserSid: "S-1-5-21-current");

        CapturedPrintJobProcessor processor = new(new CapturedPrintJobProcessorOptions
        {
            IncomingRoot = incoming,
            ProcessedRoot = Path.Combine(root, "processed"),
            HandoffRoot = Path.Combine(root, "handoff"),
            CurrentUserSidProvider = () => "S-1-5-21-current",
            PayloadStableSeconds = 0,
            PayloadStabilityProbeDelay = TimeSpan.Zero,
            PreparePdfFromCapture = WriteFakePdf,
            SelectRecipient = _ => new PrintRxerV3.Metadata.PickerSelection
            {
                RecipientName = "Chosen Pharmacy",
                RecipientEmail = "chosen@example.ie",
                Subject = "Chosen subject",
                Body = "Chosen body",
                SelectedAt = DateTimeOffset.UtcNow
            }
        });

        CapturedPrintJobResult? result = processor.ProcessOne();

        Assert.NotNull(result);
        Assert.True(result.PackageCreated);
        Assert.Equal("PackagePublished", result.Outcome);
        Assert.False(Directory.Exists(job));
    }

    [Fact]
    public void ProcessOne_defers_capture_when_submitting_sid_differs_from_current_user()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-owner-mismatch-" + Guid.NewGuid().ToString("N"));
        string incoming = Path.Combine(root, "incoming");
        string deferred = Path.Combine(root, "deferred");
        string job = WriteCapture(incoming, "20260509-120000000-job46", submittingUserSid: "S-1-5-21-other");
        bool pickerOpened = false;

        CapturedPrintJobProcessor processor = new(new CapturedPrintJobProcessorOptions
        {
            IncomingRoot = incoming,
            ProcessedRoot = Path.Combine(root, "processed"),
            DeferredRoot = deferred,
            HandoffRoot = Path.Combine(root, "handoff"),
            CurrentUserSidProvider = () => "S-1-5-21-current",
            PayloadStableSeconds = 0,
            PayloadStabilityProbeDelay = TimeSpan.Zero,
            PreparePdfFromCapture = WriteFakePdf,
            SelectRecipient = _ =>
            {
                pickerOpened = true;
                return null;
            }
        });

        CapturedPrintJobResult? result = processor.ProcessOne();

        string deferredJob = Path.Combine(deferred, "20260509-120000000-job46");
        Assert.NotNull(result);
        Assert.False(result.PackageCreated);
        Assert.Equal("JobOwnerMismatch", result.Outcome);
        Assert.False(pickerOpened);
        Assert.False(Directory.Exists(job));
        Assert.True(File.Exists(Path.Combine(deferredJob, "printrxer_v3_failure.txt")));
    }

    [Fact]
    public void ProcessOne_defers_capture_when_submitting_sid_is_missing_by_default()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-owner-missing-" + Guid.NewGuid().ToString("N"));
        string incoming = Path.Combine(root, "incoming");
        string deferred = Path.Combine(root, "deferred");
        WriteCapture(incoming, "20260509-120000000-job47", submittingUserSid: null);

        CapturedPrintJobProcessor processor = new(new CapturedPrintJobProcessorOptions
        {
            IncomingRoot = incoming,
            ProcessedRoot = Path.Combine(root, "processed"),
            DeferredRoot = deferred,
            HandoffRoot = Path.Combine(root, "handoff"),
            CurrentUserSidProvider = () => "S-1-5-21-current",
            PayloadStableSeconds = 0,
            PayloadStabilityProbeDelay = TimeSpan.Zero
        });

        CapturedPrintJobResult? result = processor.ProcessOne();

        Assert.NotNull(result);
        Assert.False(result.PackageCreated);
        Assert.Equal("JobOwnerMismatch", result.Outcome);
        Assert.True(Directory.Exists(Path.Combine(deferred, "20260509-120000000-job47")));
    }

    [Fact]
    public void ProcessOne_allows_missing_submitting_sid_only_with_explicit_import_override()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-owner-import-" + Guid.NewGuid().ToString("N"));
        string incoming = Path.Combine(root, "incoming");
        WriteCapture(incoming, "20260509-120000000-job48", submittingUserSid: null);

        CapturedPrintJobProcessor processor = new(new CapturedPrintJobProcessorOptions
        {
            IncomingRoot = incoming,
            ProcessedRoot = Path.Combine(root, "processed"),
            HandoffRoot = Path.Combine(root, "handoff"),
            AllowMissingJobOwnerForImport = true,
            PayloadStableSeconds = 0,
            PayloadStabilityProbeDelay = TimeSpan.Zero,
            PreparePdfFromCapture = WriteFakePdf,
            SelectRecipient = _ => new PrintRxerV3.Metadata.PickerSelection
            {
                RecipientName = "Chosen Pharmacy",
                RecipientEmail = "chosen@example.ie",
                Subject = "Chosen subject",
                Body = "Chosen body",
                SelectedAt = DateTimeOffset.UtcNow
            }
        });

        CapturedPrintJobResult? result = processor.ProcessOne();

        Assert.NotNull(result);
        Assert.True(result.PackageCreated);
    }

    [Fact]
    public void ProcessOne_waits_when_payload_is_missing_or_zero_or_recent()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-payload-wait-" + Guid.NewGuid().ToString("N"));
        string incoming = Path.Combine(root, "incoming");
        string missing = WriteCapture(incoming, "20260509-120000000-job49", createPayload: false);
        string zero = WriteCapture(incoming, "20260509-120001000-job50", payloadText: string.Empty);
        string recent = WriteCapture(incoming, "20260509-120002000-job51");
        int pickerCalls = 0;

        CapturedPrintJobProcessor processor = new(new CapturedPrintJobProcessorOptions
        {
            IncomingRoot = incoming,
            ProcessedRoot = Path.Combine(root, "processed"),
            HandoffRoot = Path.Combine(root, "handoff"),
            CurrentUserSidProvider = () => "S-1",
            PayloadStableSeconds = 30,
            PayloadStabilityProbeDelay = TimeSpan.Zero,
            SelectRecipient = _ =>
            {
                pickerCalls++;
                return null;
            }
        });

        Assert.Null(processor.ProcessOne());
        Assert.Equal(0, pickerCalls);
        Assert.True(Directory.Exists(missing));
        Assert.True(Directory.Exists(zero));
        Assert.True(Directory.Exists(recent));
    }

    [Fact]
    public void ProcessOne_defers_unstable_payload_after_metadata_grace_period()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-payload-stale-" + Guid.NewGuid().ToString("N"));
        string incoming = Path.Combine(root, "incoming");
        string deferred = Path.Combine(root, "deferred");
        string job = WriteCapture(incoming, "20260509-120000000-job52", createPayload: false);
        File.SetLastWriteTimeUtc(Path.Combine(job, "metadata.json"), DateTime.UtcNow.AddMinutes(-5));

        CapturedPrintJobProcessor processor = new(new CapturedPrintJobProcessorOptions
        {
            IncomingRoot = incoming,
            ProcessedRoot = Path.Combine(root, "processed"),
            DeferredRoot = deferred,
            HandoffRoot = Path.Combine(root, "handoff"),
            MetadataGraceSeconds = 1,
            PayloadStabilityProbeDelay = TimeSpan.Zero
        });

        Assert.Null(processor.ProcessOne());
        Assert.False(Directory.Exists(job));
        Assert.True(File.Exists(Path.Combine(deferred, "20260509-120000000-job52", "printrxer_v3_failure.txt")));
    }

    private static string WriteFakePdf(string captureDirectory, string payloadPath)
    {
        string pdfPath = Path.Combine(captureDirectory, "prescription.pdf");
        File.WriteAllText(pdfPath, "%PDF-1.4\n% test pdf\n");
        return pdfPath;
    }

    private static string WritePendingPackage(string outbox, string packageId)
    {
        Directory.CreateDirectory(outbox);
        string pdfPath = Path.Combine(outbox, packageId + ".pdf");
        File.WriteAllText(pdfPath, "%PDF-1.4\n% test pdf\n");
        DateTimeOffset timestamp = new(2026, 5, 9, 15, 0, 0, TimeSpan.Zero);
        PackageRequest request = PackageRequestBuilder.Create(
            packageId,
            Sha256Hasher.HashFile(pdfPath),
            timestamp,
            new WorkstationIdentity { WindowsUser = "GAVIN", DomainUser = "DOMAIN\\GAVIN", UserSid = "S-1", SessionId = 1, WorkstationName = "WS01", WorkstationDomain = "DOMAIN" },
            new PrintJobOrigin { Source = "sample" },
            new PickerSelection { RecipientName = "Beta", RecipientEmail = "beta@example.ie", Subject = "Subject", Body = "Body", SelectedAt = timestamp });

        return HandoffPackageWriter.Write(outbox, request, pdfPath);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
    }

    private static CapturedPrintJobProcessor CreatePublishTestProcessor(
        string root,
        string incoming,
        string handoff,
        string outbox,
        string published,
        Action<string>? log = null,
        Func<string>? packageIdProvider = null,
        string? failedRoot = null)
    {
        return new CapturedPrintJobProcessor(new CapturedPrintJobProcessorOptions
        {
            IncomingRoot = incoming,
            ProcessedRoot = Path.Combine(root, "processed"),
            DeferredRoot = Path.Combine(root, "deferred"),
            LocalOutboxRoot = outbox,
            PublishedRoot = published,
            FailedRoot = failedRoot ?? Path.Combine(root, "failed"),
            HandoffRoot = handoff,
            CurrentUserSidProvider = () => "S-1",
            PayloadStableSeconds = 0,
            PayloadStabilityProbeDelay = TimeSpan.Zero,
            Log = log,
            PackageIdProvider = packageIdProvider,
            PreparePdfFromCapture = WriteFakePdf,
            SelectRecipient = _ => new PrintRxerV3.Metadata.PickerSelection
            {
                RecipientName = "Chosen Pharmacy",
                RecipientEmail = "chosen@example.ie",
                Subject = "Chosen subject",
                Body = "Chosen body",
                SelectedAt = DateTimeOffset.UtcNow
            }
        });
    }

    private static string WriteCapture(string incomingRoot, string name, string? submittingUserSid = "S-1", bool createPayload = true, string payloadText = "payload")
    {
        string job = Path.Combine(incomingRoot, name);
        Directory.CreateDirectory(job);
        if (createPayload)
        {
            File.WriteAllText(Path.Combine(job, "job.xps"), payloadText);
        }

        string sidProperty = submittingUserSid is null
            ? string.Empty
            : "  \"SubmittingUserSid\": \"" + submittingUserSid + "\"," + Environment.NewLine;
        File.WriteAllText(Path.Combine(job, "metadata.json"),
            "{" + Environment.NewLine +
            "  \"source\": \"PrintRxer.PortMonitor\"," + Environment.NewLine +
            "  \"printerName\": \"printRxer\"," + Environment.NewLine +
            "  \"documentName\": \"document\"," + Environment.NewLine +
            "  \"jobId\": 45," + Environment.NewLine +
            "  \"SubmittingUser\": \"gavin\"," + Environment.NewLine +
            sidProperty +
            "  \"capturedAtUtc\": \"2026-05-09T12:00:00.000Z\"," + Environment.NewLine +
            "  \"payloadFile\": \"job.xps\"" + Environment.NewLine +
            "}");
        return job;
    }
}
