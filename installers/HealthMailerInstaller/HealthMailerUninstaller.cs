namespace HealthMailerInstaller;

internal static class HealthMailerUninstaller
{
    public static bool IsInstalled()
    {
        return Directory.Exists(InstallerPaths.ProgramFilesRoot) ||
            Directory.Exists(InstallerPaths.LegacyProgramFilesRoot) ||
            TaskExists();
    }

    public static bool HasLocalData()
    {
        return Directory.Exists(InstallerPaths.ProgramDataRoot);
    }

    public static void Uninstall(bool removeData, Action<string> log)
    {
        log("Stopping and removing HealthMailer watcher task.");
        TryStep(() => RemoveWatcher(log), "Watcher cleanup", log);

        log("Removing installed application files.");
        if (Directory.Exists(InstallerPaths.ProgramFilesRoot))
        {
            TryStep(() => RemoveProtectedApplicationDirectory(log), "Application file cleanup", log);
        }

        if (Directory.Exists(InstallerPaths.LegacyProgramFilesRoot))
        {
            TryStep(() => DeleteDirectoryBestEffort(InstallerPaths.LegacyProgramFilesRoot, log), "Legacy application file cleanup", log);
        }

        if (removeData)
        {
            log("Removing remaining ProgramData lab-reset data after application file cleanup.");
            if (Directory.Exists(InstallerPaths.ProgramDataRoot))
            {
                TryStep(() => DeleteDirectoryBestEffort(InstallerPaths.ProgramDataRoot, log), "Final ProgramData cleanup", log);
            }
        }
        else
        {
            log("Preserving ProgramData evidence except installed application files: " + InstallerPaths.ProgramDataRoot);
        }
    }

    private static void RemoveProtectedApplicationDirectory(Action<string> log)
    {
        log("Preparing hardened HealthMailer application folder for removal.");
        InstallerSecurity.PrepareApplicationDirectoryForRemoval(InstallerPaths.ProgramFilesRoot);
        DeleteProtectedApplicationDirectory(InstallerPaths.ProgramFilesRoot);
    }

    private static void DeleteProtectedApplicationDirectory(string path)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt < 2)
                {
                    Thread.Sleep(500);
                    continue;
                }

                InstallerSecurity.HardenApplicationDirectory(path);
                throw new IOException(
                    "Could not remove the protected HealthMailer application folder. A Windows process may still hold HealthMailer.exe. Restart Windows and rerun uninstall.",
                    ex);
            }
        }
    }

    private static void TryStep(Action action, string name, Action<string> log)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            log(name + " did not complete fully. Windows may release remaining files after restart. " + FriendlyMessage(ex));
        }
    }

    private static string FriendlyMessage(Exception ex)
    {
        string message = ex.Message;
        if (message.Contains("powershell.exe", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("exited with code", StringComparison.OrdinalIgnoreCase))
        {
            return "A Windows cleanup command returned a non-zero exit code.";
        }

        return message;
    }

    private static void RemoveWatcher(Action<string> log)
    {
        string command = @"
Get-ScheduledTask -TaskName 'HealthMailer' -ErrorAction SilentlyContinue | ForEach-Object {
    Stop-ScheduledTask -TaskName 'HealthMailer' -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName 'HealthMailer' -Confirm:$false -ErrorAction SilentlyContinue
}
Get-Process -Name 'HealthMailer' -ErrorAction SilentlyContinue | Stop-Process -Force
$deadline = (Get-Date).AddSeconds(10)
while ((Get-Process -Name 'HealthMailer' -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 250
}
if (Get-Process -Name 'HealthMailer' -ErrorAction SilentlyContinue) {
    throw 'HealthMailer process did not stop before uninstall. Restart Windows and rerun uninstall.'
}
";
        string output = ProcessRunner.PowerShell(command, requireSuccess: false);
        LogOutput(output, log);
    }

    private static bool TaskExists()
    {
        return ProcessRunner.PowerShell("if (Get-ScheduledTask -TaskName 'HealthMailer' -ErrorAction SilentlyContinue) { 'true' }", requireSuccess: false).Contains("true", StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteDirectoryBestEffort(string path, Action<string> log)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 2)
            {
                Thread.Sleep(500);
            }
            catch (UnauthorizedAccessException) when (attempt < 2)
            {
                Thread.Sleep(500);
            }
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            string command = @"
$path = '" + path.Replace("'", "''", StringComparison.Ordinal) + @"'
$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
icacls $path /grant:r ""${identity}:(OI)(CI)F"" /T /C | Out-String
Get-ChildItem -LiteralPath $path -Force -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
    icacls $_.FullName /grant:r ""${identity}:F"" /C | Out-String
}
Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop
";
            string output = ProcessRunner.PowerShell(command, requireSuccess: false);
            LogOutput(output, log);
            if (Directory.Exists(path))
            {
                log("Could not remove " + path + " automatically. It may be in use and can be removed after restart. " + ex.Message);
            }
        }
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
