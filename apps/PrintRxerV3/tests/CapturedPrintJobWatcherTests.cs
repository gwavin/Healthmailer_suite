using PrintRxerV3.Capture;
using Xunit;

namespace PrintRxerV3.Tests;

public sealed class CapturedPrintJobWatcherTests
{
    [Fact]
    public void RunUntilIdle_processes_available_jobs_and_notifies_for_each_package()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-watch-" + Guid.NewGuid().ToString("N"));
        string incoming = Path.Combine(root, "incoming");
        string processed = Path.Combine(root, "processed");
        string handoff = Path.Combine(root, "handoff");
        WriteCapture(incoming, "20260509-120000000-job1", 1);
        WriteCapture(incoming, "20260509-120001000-job2", 2);
        List<string> notifications = new();

        CapturedPrintJobWatcher watcher = new(new CapturedPrintJobWatcherOptions
        {
            Processor = new CapturedPrintJobProcessor(new CapturedPrintJobProcessorOptions
            {
                IncomingRoot = incoming,
                ProcessedRoot = processed,
                HandoffRoot = handoff,
                AllowMissingJobOwnerForImport = true,
                PayloadStableSeconds = 0,
                PayloadStabilityProbeDelay = TimeSpan.Zero,
                PreparePdfFromCapture = WriteFakePdf
            }),
            NotifyPackageReady = packageDirectory => notifications.Add(packageDirectory),
            MaxIdlePolls = 1,
            PollInterval = TimeSpan.Zero
        });

        int processedCount = watcher.RunUntilIdle();

        Assert.Equal(2, processedCount);
        Assert.Equal(2, notifications.Count);
        Assert.Empty(Directory.EnumerateDirectories(incoming));
        Assert.Equal(2, Directory.EnumerateDirectories(processed).Count());
        Assert.Equal(2, Directory.EnumerateDirectories(handoff).Count());
    }

    [Fact]
    public void RunUntilIdle_does_not_notify_when_selection_is_cancelled()
    {
        string root = Path.Combine(Path.GetTempPath(), "printrxer-v3-watch-cancel-" + Guid.NewGuid().ToString("N"));
        string incoming = Path.Combine(root, "incoming");
        string processed = Path.Combine(root, "processed");
        string handoff = Path.Combine(root, "handoff");
        WriteCapture(incoming, "20260509-120000000-job1", 1);
        List<string> notifications = new();

        CapturedPrintJobWatcher watcher = new(new CapturedPrintJobWatcherOptions
        {
            Processor = new CapturedPrintJobProcessor(new CapturedPrintJobProcessorOptions
            {
                IncomingRoot = incoming,
                ProcessedRoot = processed,
                HandoffRoot = handoff,
                AllowMissingJobOwnerForImport = true,
                PayloadStableSeconds = 0,
                PayloadStabilityProbeDelay = TimeSpan.Zero,
                SelectRecipient = _ => null
            }),
            NotifyPackageReady = packageDirectory => notifications.Add(packageDirectory),
            MaxIdlePolls = 1,
            PollInterval = TimeSpan.Zero
        });

        int processedCount = watcher.RunUntilIdle();

        Assert.Equal(1, processedCount);
        Assert.Empty(notifications);
        Assert.Empty(Directory.EnumerateDirectories(incoming));
    }

    private static void WriteCapture(string incomingRoot, string name, int jobId)
    {
        string job = Path.Combine(incomingRoot, name);
        Directory.CreateDirectory(job);
        File.WriteAllText(Path.Combine(job, "job.xps"), "payload " + jobId);
        File.WriteAllText(Path.Combine(job, "metadata.json"), $$"""
            {
              "source": "PrintRxer.PortMonitor",
              "printerName": "printRxer",
              "documentName": "document",
              "jobId": {{jobId}},
              "payloadFile": "job.xps"
            }
            """);
    }

    private static string WriteFakePdf(string captureDirectory, string payloadPath)
    {
        string pdfPath = Path.Combine(captureDirectory, "prescription.pdf");
        File.WriteAllText(pdfPath, "%PDF-1.4\n% test pdf\n");
        return pdfPath;
    }
}
