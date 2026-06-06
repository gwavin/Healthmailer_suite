namespace PrintRxerV3Installer;

internal static class PrintRxerUninstaller
{
    public static bool IsInstalled()
    {
        return Directory.Exists(InstallerPaths.ProgramFilesRoot) ||
            TaskExists() ||
            PrinterStackExists();
    }

    public static bool HasLocalData()
    {
        return Directory.Exists(InstallerPaths.ProgramDataRoot);
    }

    public static void Uninstall(bool removeData, Action<string> log)
    {
        log("Starting printRxer uninstall.");
        log("Initial component state: " + ComponentStateSummary());

        log("Stopping and removing printRxer watcher task.");
        TryStep(() => RemoveWatcher(log), "Watcher cleanup", log);
        log("Watcher cleanup step finished.");

        log("Removing printRxer capture printer.");
        TryStep(() => RemoveCapturePrinter(log), "Printer cleanup", log);
        log("Printer cleanup step finished.");

        if (removeData)
        {
            log("Removing ProgramData lab-reset data.");
            TryStep(() => DeleteDirectoryBestEffort(InstallerPaths.ProgramDataRoot, log), "ProgramData cleanup", log);
        }
        else
        {
            log("Preserving ProgramData: " + InstallerPaths.ProgramDataRoot);
        }

        log("Removing installed application files.");
        if (Directory.Exists(InstallerPaths.ProgramFilesRoot))
        {
            TryStep(() => DeleteDirectoryBestEffort(InstallerPaths.ProgramFilesRoot, log), "Application file cleanup", log);
        }
        else
        {
            log("Installed application folder was already absent: " + InstallerPaths.ProgramFilesRoot);
        }

        if (removeData && Directory.Exists(InstallerPaths.ProgramDataRoot))
        {
            log("Checking for late-created ProgramData folders.");
            TryStep(() => RemoveWatcher(log), "Late watcher cleanup", log);
            TryStep(() => DeleteDirectoryBestEffort(InstallerPaths.ProgramDataRoot, log), "Final ProgramData cleanup", log);
        }

        if (removeData)
        {
            RemoveLateRecreatedProgramData(log);
        }

        log("Final component state: " + ComponentStateSummary());
    }

    private static string ComponentStateSummary()
    {
        return "appFiles=" + Directory.Exists(InstallerPaths.ProgramFilesRoot) +
            ", programData=" + Directory.Exists(InstallerPaths.ProgramDataRoot) +
            ", scheduledTask=" + TaskExists() +
            ", printerStack=" + PrinterStackExists();
    }

    private static void TryStep(Action action, string name, Action<string> log)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            log(name + " did not complete fully. Windows may release remaining files or printer components after restart. " + FriendlyMessage(ex));
        }
    }

    private static string FriendlyMessage(Exception ex)
    {
        string message = ex.Message;
        if (message.Contains("powershell.exe", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("exited with code", StringComparison.OrdinalIgnoreCase))
        {
            return "A Windows printer cleanup command returned a non-zero exit code.";
        }

        return message;
    }

    private static bool TaskExists()
    {
        return ProcessRunner.PowerShell("if (Get-ScheduledTask -TaskName 'printRxer' -ErrorAction SilentlyContinue) { 'true'; exit }; if (Get-ScheduledTask -TaskName 'PrintRxerV3' -ErrorAction SilentlyContinue) { 'true' }", requireSuccess: false).Contains("true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool PrinterStackExists()
    {
        string command = @"
if (Get-Printer -Name 'printRxer' -ErrorAction SilentlyContinue) { 'true'; exit }
if (Get-PrinterPort -Name 'printrx:' -ErrorAction SilentlyContinue) { 'true'; exit }
if (Get-PrinterDriver -Name 'PrintRxer XPS Driver' -ErrorAction SilentlyContinue) { 'true'; exit }
if (Test-Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Print\Monitors\PrintRxer Port Monitor') { 'true'; exit }
";
        return ProcessRunner.PowerShell(command, requireSuccess: false).Contains("true", StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteDirectoryBestEffort(string path, Action<string> log)
    {
        if (!Directory.Exists(path))
        {
            log("Folder was already absent: " + path);
            return;
        }

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                log("Deleting folder: " + path);
                Directory.Delete(path, recursive: true);
                log("Deleted folder: " + path);
                return;
            }
            catch (IOException) when (attempt < 2)
            {
                log("Folder is still in use; retrying: " + path);
                Thread.Sleep(500);
            }
            catch (UnauthorizedAccessException) when (attempt < 2)
            {
                log("Folder access is not yet released; retrying: " + path);
                Thread.Sleep(500);
            }
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            string command = "Remove-Item -LiteralPath '" + path.Replace("'", "''", StringComparison.Ordinal) + "' -Recurse -Force -ErrorAction Stop";
            string output = ProcessRunner.PowerShell(command, requireSuccess: false);
            LogOutput(output, log);
            if (Directory.Exists(path))
            {
                log("Could not remove " + path + " automatically. It may be in use and can be removed after restart. " + ex.Message);
            }
        }
    }

    private static void RemoveLateRecreatedProgramData(Action<string> log)
    {
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            Thread.Sleep(1000);
            if (!Directory.Exists(InstallerPaths.ProgramDataRoot))
            {
                return;
            }

            log("ProgramData was recreated after deletion; retrying approved lab-reset removal. Attempt " + attempt + " of 5.");
            LogProgramDataChildren(log);
            TryStep(() => DeleteDirectoryBestEffort(InstallerPaths.ProgramDataRoot, log), "Late ProgramData cleanup", log);
        }
    }

    private static void LogProgramDataChildren(Action<string> log)
    {
        try
        {
            foreach (string child in Directory.EnumerateFileSystemEntries(InstallerPaths.ProgramDataRoot).Take(20))
            {
                log("Remaining ProgramData child: " + child);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log("Could not enumerate recreated ProgramData before retry: " + ex.Message);
        }
    }

    private static void RemoveWatcher(Action<string> log)
    {
        string command = @"
$taskNames = @('printRxer', 'PrintRxerV3', 'PrintRxer Agent')
foreach ($taskName in $taskNames) {
    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($task) {
        Write-Output ('Removing scheduled task: ' + $taskName)
        Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    } else {
        Write-Output ('Scheduled task not present: ' + $taskName)
    }
}
$processes = Get-Process -Name 'printRxer' -ErrorAction SilentlyContinue
if ($processes) {
    Write-Output 'Stopping running printRxer process.'
    $processes | Stop-Process -Force
} else {
    Write-Output 'No running printRxer process found.'
}
$agentProcesses = Get-Process -Name 'PrintRxer.Agent' -ErrorAction SilentlyContinue
if ($agentProcesses) {
    Write-Output 'Stopping running PrintRxer.Agent process.'
    $agentProcesses | Stop-Process -Force
} else {
    Write-Output 'No running PrintRxer.Agent process found.'
}
";
        string output = ProcessRunner.PowerShell(command, requireSuccess: false);
        LogOutput(output, log);
    }

    private static void RemoveCapturePrinter(Action<string> log)
    {
        string script = Path.Combine(InstallerPaths.PayloadToolsRoot, "Uninstall-PrintRxerCapturePrinter.ps1");
        if (File.Exists(script))
        {
            string output = ProcessRunner.PowerShellFile(script, requireSuccess: false);
            LogOutput(output, log);

            return;
        }

        string fallback = @"
$printer = Get-Printer -Name 'printRxer' -ErrorAction SilentlyContinue
if ($printer) {
    Write-Output 'Removing printRxer printer queue.'
    Get-PrintJob -PrinterName 'printRxer' -ErrorAction SilentlyContinue | Remove-PrintJob -ErrorAction SilentlyContinue
    Remove-Printer -Name 'printRxer' -ErrorAction SilentlyContinue
} else {
    Write-Output 'printRxer printer queue not present.'
}
$port = Get-PrinterPort -Name 'printrx:' -ErrorAction SilentlyContinue
if ($port) { Write-Output 'Removing printrx: printer port.'; Remove-PrinterPort -Name 'printrx:' -ErrorAction SilentlyContinue } else { Write-Output 'printrx: printer port not present.' }
$driver = Get-PrinterDriver -Name 'PrintRxer XPS Driver' -ErrorAction SilentlyContinue
if ($driver) { Write-Output 'Removing PrintRxer XPS Driver.'; Remove-PrinterDriver -Name 'PrintRxer XPS Driver' -ErrorAction SilentlyContinue } else { Write-Output 'PrintRxer XPS Driver not present.' }
Write-Output 'Removing PrintRxer Port Monitor registry entry if present.'
Remove-Item -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Print\Monitors\PrintRxer Port Monitor' -Recurse -Force -ErrorAction SilentlyContinue
Write-Output 'Removing PrintRxer Port Monitor DLL if present.'
Remove-Item -LiteralPath (Join-Path $env:WINDIR 'System32\PrintRxerPortMonitor.dll') -Force -ErrorAction SilentlyContinue
";
        string fallbackOutput = ProcessRunner.PowerShell(fallback, requireSuccess: false);
        LogOutput(fallbackOutput, log);
    }

    private static void LogOutput(string output, Action<string> log)
    {
        if (!string.IsNullOrWhiteSpace(output))
        {
            foreach (string line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            {
                log(line);
            }
        }
    }
}
