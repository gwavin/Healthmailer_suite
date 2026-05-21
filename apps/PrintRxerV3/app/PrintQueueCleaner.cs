using System.Diagnostics;

namespace PrintRxerV3.App;

public static class PrintQueueCleaner
{
    public static void RemoveCompletedPrintRxerJobs()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string command = "Get-PrintJob -PrinterName 'printRxer' -ErrorAction SilentlyContinue | " +
            "Where-Object { $_.JobStatus -match 'Complete' } | Remove-PrintJob -ErrorAction SilentlyContinue";
        using Process? process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" + command.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        });

        if (process is null)
        {
            throw new InvalidOperationException("Could not start PowerShell for print queue cleanup.");
        }

        process.WaitForExit(10000);
        if (!process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw new TimeoutException("Print queue cleanup timed out.");
        }
    }
}
