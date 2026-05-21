using System.Text.Json;
using Xunit;

namespace HealthMailer.Tests;

public sealed class HandoffWatcherTests
{
    [Fact]
    public async Task RunAsync_retries_file_watcher_start_after_handoff_folder_becomes_available()
    {
        string root = Path.Combine(Path.GetTempPath(), "healthmailer-watcher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string handoffRoot = Path.Combine(root, "handoff");
        File.WriteAllText(handoffRoot, "temporarily blocks folder creation");
        List<string> logs = [];
        object sync = new();
        HealthMailerConfig config = new()
        {
            HandoffRoot = handoffRoot,
            LocalRoot = Path.Combine(root, "local"),
            PollIntervalSeconds = 1,
            SendMail = false
        };

        PackageProcessor processor = new(config, new RecordingMailer(), message =>
        {
            lock (sync)
            {
                logs.Add(message);
            }
        });
        using HandoffWatcher watcher = new(config, processor, message =>
        {
            lock (sync)
            {
                logs.Add(message);
            }
        });
        using CancellationTokenSource cancellation = new();

        Task runTask = watcher.RunAsync(cancellation.Token);
        await WaitUntilAsync(() => ContainsLog(logs, sync, "File watcher not started"), TimeSpan.FromSeconds(5));

        File.Delete(handoffRoot);
        Directory.CreateDirectory(handoffRoot);

        await WaitUntilAsync(() => ContainsLog(logs, sync, "File watcher started"), TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await IgnoreCancellationAsync(runTask);
    }

    [Fact]
    public async Task RunAsync_processes_package_after_unavailable_handoff_folder_becomes_available()
    {
        string root = Path.Combine(Path.GetTempPath(), "healthmailer-watcher-process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string handoffRoot = Path.Combine(root, "handoff");
        File.WriteAllText(handoffRoot, "temporarily blocks folder creation");
        List<string> logs = [];
        object sync = new();
        RecordingMailer mailer = new();
        HealthMailerConfig config = new()
        {
            HandoffRoot = handoffRoot,
            LocalRoot = Path.Combine(root, "local"),
            PollIntervalSeconds = 1,
            SendMail = true
        };

        PackageProcessor processor = new(config, mailer, message =>
        {
            lock (sync)
            {
                logs.Add(message);
            }
        });
        using HandoffWatcher watcher = new(config, processor, message =>
        {
            lock (sync)
            {
                logs.Add(message);
            }
        });
        using CancellationTokenSource cancellation = new();

        Task runTask = watcher.RunAsync(cancellation.Token);
        await WaitUntilAsync(() => ContainsLog(logs, sync, "File watcher not started"), TimeSpan.FromSeconds(5));

        File.Delete(handoffRoot);
        Directory.CreateDirectory(handoffRoot);
        CreatePackage(handoffRoot, "pkg-after-return");

        await WaitUntilAsync(() => mailer.SentCount == 1, TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await IgnoreCancellationAsync(runTask);
    }

    private static bool ContainsLog(List<string> logs, object sync, string text)
    {
        lock (sync)
        {
            return logs.Any(line => line.Contains(text, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.True(condition(), "Condition was not met before timeout.");
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class RecordingMailer : IMailHandoff
    {
        private readonly object _sync = new();
        private readonly List<DeliveryPackage> _sent = [];

        public int SentCount
        {
            get
            {
                lock (_sync)
                {
                    return _sent.Count;
                }
            }
        }

        public void Send(DeliveryPackage package)
        {
            lock (_sync)
            {
                _sent.Add(package);
            }
        }
    }

    private static string CreatePackage(string root, string packageId)
    {
        string packageDirectory = Path.Combine(root, packageId);
        Directory.CreateDirectory(packageDirectory);
        string pdfPath = Path.Combine(packageDirectory, "prescription.pdf");
        File.WriteAllText(pdfPath, "%PDF-1.4\n% test\n");
        string hash = SecurityUtilities.ComputeSha256(pdfPath);
        File.WriteAllText(Path.Combine(packageDirectory, "request.json"), JsonSerializer.Serialize(new
        {
            packageId,
            selectedRecipientEmail = "recipient@example.ie",
            selectedRecipientName = "Recipient",
            subject = "Prescription",
            body = "Please see attached.",
            pdfSha256 = hash
        }));
        File.WriteAllText(Path.Combine(packageDirectory, "request.sha256"), hash + "  prescription.pdf");
        File.WriteAllText(Path.Combine(packageDirectory, "summary.txt"), "printRxer handoff package");
        File.WriteAllText(Path.Combine(packageDirectory, "READY"), string.Empty);
        return packageDirectory;
    }
}
