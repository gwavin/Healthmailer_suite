using System.Security.Principal;
using System.Text.Json;
using PrintRxerV3.Documents;
using PrintRxerV3.Handoff;
using PrintRxerV3.Metadata;
using PrintRxerV3.Packaging;

namespace PrintRxerV3.Capture;

public sealed class CapturedPrintJobProcessor
{
    private readonly CapturedPrintJobProcessorOptions _options;
    private readonly HandoffPackagePublisher _publisher = new();

    public CapturedPrintJobProcessor(CapturedPrintJobProcessorOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public CapturedPrintJobResult? ProcessOne()
    {
        Directory.CreateDirectory(_options.IncomingRoot);
        Directory.CreateDirectory(_options.ProcessedRoot);
        Directory.CreateDirectory(GetDeferredRoot());
        Directory.CreateDirectory(GetLocalOutboxRoot());
        Directory.CreateDirectory(GetPublishedRoot());
        Directory.CreateDirectory(GetFailedRoot());

        _publisher.TryPublishPending(GetLocalOutboxRoot(), _options.HandoffRoot, GetPublishedRoot());

        string? captureDirectory = null;
        CaptureMetadata? metadata = null;
        string? payloadPath = null;
        foreach (string candidate in Directory.EnumerateDirectories(_options.IncomingRoot).OrderBy(path => Directory.GetLastWriteTimeUtc(path)))
        {
            CaptureReadiness readiness = EvaluateCaptureReadiness(candidate, out CaptureMetadata? candidateMetadata, out string? candidatePayloadPath);
            if (readiness == CaptureReadiness.Ready)
            {
                captureDirectory = candidate;
                metadata = candidateMetadata;
                payloadPath = candidatePayloadPath;
                break;
            }

            if (readiness == CaptureReadiness.Wait)
            {
                return null;
            }
        }

        if (captureDirectory is null)
        {
            return null;
        }

        if (metadata is null || payloadPath is null)
        {
            return null;
        }

        string? ownerFailure = ValidateJobOwner(metadata);
        if (ownerFailure is not null)
        {
            WriteFailureSummary(captureDirectory, "JobOwnerMismatch", ownerFailure);
            string deferredDirectory = MoveDirectory(captureDirectory, GetDeferredRoot());
            return new CapturedPrintJobResult
            {
                CaptureDirectory = captureDirectory,
                ProcessedCaptureDirectory = deferredDirectory,
                PackageDirectory = null,
                LocalPackageDirectory = null,
                PackageCreated = false,
                Outcome = "JobOwnerMismatch"
            };
        }

        TryCleanupCompletedPrintJobs();
        PickerSelection? selection = SelectRecipient(captureDirectory, payloadPath, metadata);
        if (selection is null)
        {
            string deferredDirectory = MoveDirectory(captureDirectory, GetDeferredRoot());
            return new CapturedPrintJobResult
            {
                CaptureDirectory = captureDirectory,
                ProcessedCaptureDirectory = deferredDirectory,
                PackageDirectory = null,
                LocalPackageDirectory = null,
                PackageCreated = false,
                Outcome = "RecipientSelectionCancelled"
            };
        }

        string packageId = (_options.PackageIdProvider ?? (() => PackageIdGenerator.Create(DateTimeOffset.UtcNow))).Invoke();
        string packagePdfPath;
        try
        {
            packagePdfPath = PreparePreviewPdfFromCapture(captureDirectory, payloadPath);
        }
        catch (Exception ex)
        {
            WriteFailureSummary(captureDirectory, "RenderFailed", ex);
            string deferredDirectory = MoveDirectory(captureDirectory, GetDeferredRoot());
            return new CapturedPrintJobResult
            {
                CaptureDirectory = captureDirectory,
                ProcessedCaptureDirectory = deferredDirectory,
                PackageDirectory = null,
                LocalPackageDirectory = null,
                PackageCreated = false,
                Outcome = "RenderFailed"
            };
        }

        string hash = Sha256Hasher.HashFile(packagePdfPath);
        PackageRequest request = PackageRequestBuilder.Create(
            packageId,
            hash,
            DateTimeOffset.UtcNow,
            CaptureWorkstationIdentity(),
            metadata.ToPrintJobOrigin(ClinicalDocumentMetadata.FromGlyphText(XpsTextExtractor.ExtractGlyphText(payloadPath))),
            selection with
            {
                Body = AppendRecipientToBody(selection.Body, selection)
            });

        string localPackageDirectory = HandoffPackageWriter.Write(GetLocalOutboxRoot(), request, packagePdfPath);
        _options.Log?.Invoke("PackageQueuedLocal: " + localPackageDirectory);
        HandoffPublishResult publishResult = _publisher.TryPublish(localPackageDirectory, _options.HandoffRoot, GetPublishedRoot());
        if (!publishResult.Published)
        {
            if (publishResult.Outcome.Equals("PackagePublishFailed", StringComparison.Ordinal))
            {
                string failedDirectory = MovePackageToFailed(localPackageDirectory, publishResult);
                localPackageDirectory = failedDirectory;
                _options.Log?.Invoke("PackagePublishFailed: PrintRxer package moved to failed. " + publishResult.Message);
            }
            else
            {
                _options.Log?.Invoke("PackagePublishDeferred: Package queued locally; handoff folder unavailable; will retry automatically. " + publishResult.Message);
            }
        }
        else
        {
            _options.Log?.Invoke("PackagePublished: " + publishResult.PublishedDirectory);
        }

        string processedDirectory = MoveDirectory(captureDirectory, _options.ProcessedRoot);
        return new CapturedPrintJobResult
        {
            CaptureDirectory = captureDirectory,
            ProcessedCaptureDirectory = processedDirectory,
            PackageDirectory = publishResult.Published ? publishResult.PublishedDirectory : null,
            LocalPackageDirectory = localPackageDirectory,
            PackageCreated = true,
            Outcome = publishResult.Published ? "PackagePublished" : publishResult.Outcome
        };
    }

    public HandoffPublishResult RetryPendingPublication()
    {
        Directory.CreateDirectory(GetLocalOutboxRoot());
        Directory.CreateDirectory(GetPublishedRoot());
        HandoffPublishResult result = _publisher.TryPublishPending(GetLocalOutboxRoot(), _options.HandoffRoot, GetPublishedRoot());
        if (result.Outcome.Equals("PackagePublishFailed", StringComparison.Ordinal))
        {
            string localPackageDirectory = Path.Combine(GetLocalOutboxRoot(), result.PackageId);
            if (Directory.Exists(localPackageDirectory))
            {
                string failedDirectory = MovePackageToFailed(localPackageDirectory, result);
                _options.Log?.Invoke("PackagePublishFailed: PrintRxer pending package moved to failed. " + failedDirectory + " " + result.Message);
            }
        }

        return result;
    }

    private string MovePackageToFailed(string localPackageDirectory, HandoffPublishResult publishResult)
    {
        WriteFailureSummary(localPackageDirectory, publishResult.Outcome, publishResult.Message);
        return MoveDirectory(localPackageDirectory, GetFailedRoot());
    }

    private CaptureReadiness EvaluateCaptureReadiness(string directory, out CaptureMetadata? metadata, out string? payloadPath)
    {
        metadata = null;
        payloadPath = null;
        string metadataPath = Path.Combine(directory, "metadata.json");
        if (!File.Exists(metadataPath))
        {
            return CaptureReadiness.Skip;
        }

        metadata = CaptureMetadata.Load(metadataPath);
        payloadPath = ResolvePayloadPath(directory, metadata);
        if (!IsPayloadStable(payloadPath))
        {
            if (DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(metadataPath) >= TimeSpan.FromSeconds(Math.Max(1, _options.MetadataGraceSeconds)))
            {
                WriteFailureSummary(directory, "PayloadNotReady", "Payload was missing, empty, locked, or still changing after the metadata grace period.");
                MoveDirectory(directory, GetDeferredRoot());
                return CaptureReadiness.Skip;
            }

            return CaptureReadiness.Wait;
        }

        return CaptureReadiness.Ready;
    }

    private string ResolvePayloadPath(string directory, CaptureMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata.PayloadFile))
        {
            return Path.Combine(directory, metadata.PayloadFile);
        }

        string xpsPath = Path.Combine(directory, "job.xps");
        return File.Exists(xpsPath) ? xpsPath : Path.Combine(directory, "job.oxps");
    }

    private bool IsPayloadStable(string payloadPath)
    {
        if (!File.Exists(payloadPath))
        {
            return false;
        }

        FileInfo before = new(payloadPath);
        if (before.Length <= 0)
        {
            return false;
        }

        if (DateTimeOffset.UtcNow - before.LastWriteTimeUtc < TimeSpan.FromSeconds(Math.Max(0, _options.PayloadStableSeconds)))
        {
            return false;
        }

        try
        {
            using FileStream stream = new(payloadPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (_options.PayloadStabilityProbeDelay > TimeSpan.Zero)
            {
                Thread.Sleep(_options.PayloadStabilityProbeDelay);
                FileInfo after = new(payloadPath);
                return before.Length == after.Length && before.LastWriteTimeUtc == after.LastWriteTimeUtc;
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string? ValidateJobOwner(CaptureMetadata metadata)
    {
        if (!_options.RequireJobOwnerMatch)
        {
            return null;
        }

        string? submittingSid = string.IsNullOrWhiteSpace(metadata.SubmittingUserSid) ? null : metadata.SubmittingUserSid.Trim();
        if (submittingSid is null)
        {
            return _options.AllowMissingJobOwnerForImport ? null : "Capture metadata did not include submittingUserSid.";
        }

        string? currentSid = (_options.CurrentUserSidProvider ?? GetCurrentUserSid).Invoke();
        if (string.IsNullOrWhiteSpace(currentSid))
        {
            return "Current Windows user SID could not be determined.";
        }

        return string.Equals(submittingSid, currentSid.Trim(), StringComparison.OrdinalIgnoreCase)
            ? null
            : "Capture submittingUserSid does not match the current Windows user SID.";
    }

    private PickerSelection? SelectRecipient(string captureDirectory, string payloadPath, CaptureMetadata metadata)
    {
        if (_options.SelectRecipient is null)
        {
            return new PickerSelection
            {
                RecipientName = "Pending HealthMailer selection",
                RecipientEmail = "pending@example.invalid",
                Subject = "Captured print job ready for HealthMailer",
                Body = "This package was created from a captured print job and is ready for scheduled sending workflow.",
                SelectedAt = DateTimeOffset.UtcNow
            };
        }

        ClinicalDocumentMetadata clinicalMetadata = ClinicalDocumentMetadata.FromGlyphText(XpsTextExtractor.ExtractGlyphText(payloadPath));
        return _options.SelectRecipient(new CapturedPrintJobContext
        {
            CaptureDirectory = captureDirectory,
            PayloadPath = payloadPath,
            DocumentName = metadata.DocumentName ?? string.Empty,
            PrinterName = metadata.PrinterName ?? string.Empty,
            PrintJobId = metadata.JobId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            SubmittingUser = metadata.SubmittingUser ?? string.Empty,
            CapturedAtUtc = metadata.CapturedAtUtc,
            PatientName = clinicalMetadata.PatientName ?? string.Empty,
            Mrn = clinicalMetadata.Mrn ?? string.Empty,
            PrescribedBy = clinicalMetadata.PrescribedBy ?? string.Empty
        });
    }

    private void TryCleanupCompletedPrintJobs()
    {
        if (_options.CleanupCompletedPrintJobs is null)
        {
            return;
        }

        try
        {
            _options.CleanupCompletedPrintJobs();
        }
        catch (Exception ex)
        {
            _options.Log?.Invoke("PrintQueueCleanupFailed: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static string AppendRecipientToBody(string body, PickerSelection selection)
    {
        string recipientLine = "Intended recipient: " + selection.RecipientName + " <" + selection.RecipientEmail + ">";
        if ((body ?? string.Empty).Contains(selection.RecipientEmail, StringComparison.OrdinalIgnoreCase))
        {
            return body ?? string.Empty;
        }

        return (body ?? string.Empty).TrimEnd() + Environment.NewLine + Environment.NewLine + recipientLine;
    }

    private string PreparePreviewPdfFromCapture(string captureDirectory, string payloadPath)
    {
        if (!File.Exists(payloadPath))
        {
            throw new FileNotFoundException("Captured payload file not found.", payloadPath);
        }

        if (_options.PreparePdfFromCapture is not null)
        {
            string preparedPath = _options.PreparePdfFromCapture(captureDirectory, payloadPath);
        if (!HandoffPackageValidator.LooksLikePdf(preparedPath))
        {
            throw new InvalidOperationException("Prepared attachment is not a valid PDF.");
        }

            return preparedPath;
        }

        string pdfPath = Path.Combine(captureDirectory, "prescription.pdf");
        XpsPdfRenderer.RenderToPdf(payloadPath, pdfPath);
        if (!HandoffPackageValidator.LooksLikePdf(pdfPath))
        {
            throw new InvalidOperationException("Rendered attachment is not a valid PDF.");
        }

        return pdfPath;
    }

    private string MoveDirectory(string captureDirectory, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        string processedDirectory = Path.Combine(destinationRoot, Path.GetFileName(captureDirectory));
        if (Directory.Exists(processedDirectory))
        {
            processedDirectory += "-" + Guid.NewGuid().ToString("N")[..8];
        }

        Directory.Move(captureDirectory, processedDirectory);
        return processedDirectory;
    }

    private static void WriteFailureSummary(string captureDirectory, string outcome, Exception exception)
    {
        string message = outcome + Environment.NewLine +
            "CapturedAt: " + DateTimeOffset.UtcNow.ToString("O") + Environment.NewLine +
            "Error: " + exception.GetType().Name + ": " + exception.Message + Environment.NewLine;
        File.WriteAllText(Path.Combine(captureDirectory, "printRxer_failure.txt"), message);
    }

    private static void WriteFailureSummary(string captureDirectory, string outcome, string reason)
    {
        string message = outcome + Environment.NewLine +
            "CapturedAt: " + DateTimeOffset.UtcNow.ToString("O") + Environment.NewLine +
            "Reason: " + reason + Environment.NewLine;
        File.WriteAllText(Path.Combine(captureDirectory, "printRxer_failure.txt"), message);
    }

    private string GetDeferredRoot()
    {
        return string.IsNullOrWhiteSpace(_options.DeferredRoot)
            ? Path.Combine(Path.GetDirectoryName(_options.ProcessedRoot) ?? _options.ProcessedRoot, "deferred")
            : _options.DeferredRoot;
    }

    private string GetLocalOutboxRoot()
    {
        return string.IsNullOrWhiteSpace(_options.LocalOutboxRoot)
            ? Path.Combine(Path.GetDirectoryName(_options.ProcessedRoot) ?? _options.ProcessedRoot, "pending-outbox")
            : _options.LocalOutboxRoot;
    }

    private string GetPublishedRoot()
    {
        return string.IsNullOrWhiteSpace(_options.PublishedRoot)
            ? Path.Combine(Path.GetDirectoryName(_options.ProcessedRoot) ?? _options.ProcessedRoot, "published")
            : _options.PublishedRoot;
    }

    private string GetFailedRoot()
    {
        return string.IsNullOrWhiteSpace(_options.FailedRoot)
            ? Path.Combine(Path.GetDirectoryName(_options.ProcessedRoot) ?? _options.ProcessedRoot, "failed")
            : _options.FailedRoot;
    }

    private static WorkstationIdentity CaptureWorkstationIdentity()
    {
        if (OperatingSystem.IsWindows())
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            string domainUser = identity.Name ?? string.Empty;
            return new WorkstationIdentity
            {
                WindowsUser = domainUser.Contains('\\') ? domainUser.Split('\\').Last() : domainUser,
                DomainUser = domainUser,
                UserSid = identity.User?.Value ?? string.Empty,
                SessionId = System.Diagnostics.Process.GetCurrentProcess().SessionId,
                WorkstationName = Environment.MachineName,
                WorkstationDomain = Environment.UserDomainName
            };
        }

        return new WorkstationIdentity
        {
            WindowsUser = Environment.UserName,
            DomainUser = Environment.UserName,
            UserSid = string.Empty,
            SessionId = System.Diagnostics.Process.GetCurrentProcess().SessionId,
            WorkstationName = Environment.MachineName,
            WorkstationDomain = string.Empty
        };
    }

    private static string? GetCurrentUserSid()
    {
        if (!OperatingSystem.IsWindows())
        {
            return string.Empty;
        }

        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value;
    }

    private enum CaptureReadiness
    {
        Skip,
        Wait,
        Ready
    }

    private sealed record CaptureMetadata
    {
        public string? Source { get; init; }
        public string? PortName { get; init; }
        public string? PrinterName { get; init; }
        public string? DocumentName { get; init; }
        public int? JobId { get; init; }
        public string? SubmittingUser { get; init; }
        public string? SubmittingUserSid { get; init; }
        public DateTimeOffset? CapturedAtUtc { get; init; }
        public string? PayloadFile { get; init; }

        public static CaptureMetadata Load(string path)
        {
            JsonSerializerOptions options = new()
            {
                PropertyNameCaseInsensitive = true
            };

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<CaptureMetadata>(json, options) ?? new CaptureMetadata();
        }

        public PrintJobOrigin ToPrintJobOrigin(ClinicalDocumentMetadata? clinicalMetadata = null)
        {
            return new PrintJobOrigin
            {
                Source = Source,
                PrinterName = PrinterName,
                DocumentName = DocumentName,
                PrintJobId = JobId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CapturedAtUtc = CapturedAtUtc,
                SubmittingUser = SubmittingUser,
                SubmittingUserSid = SubmittingUserSid,
                PatientName = clinicalMetadata?.PatientName,
                Mrn = clinicalMetadata?.Mrn,
                PrescribedBy = clinicalMetadata?.PrescribedBy
            };
        }
    }
}
