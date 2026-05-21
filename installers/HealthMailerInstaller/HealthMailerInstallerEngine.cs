using System.Text.Json;

namespace HealthMailerInstaller;

internal static class HealthMailerInstallerEngine
{
    public static void Install(InstallOptions options, Action<string> log)
    {
        if (!Directory.Exists(InstallerPaths.PayloadPublishRoot))
        {
            throw new DirectoryNotFoundException("The installer payload is missing: " + InstallerPaths.PayloadPublishRoot);
        }

        log("Creating local folders.");
        Directory.CreateDirectory(InstallerPaths.ProgramFilesRoot);
        Directory.CreateDirectory(InstallerPaths.ProgramDataRoot);
        Directory.CreateDirectory(Path.Combine(InstallerPaths.ProgramDataRoot, "sent"));
        Directory.CreateDirectory(Path.Combine(InstallerPaths.ProgramDataRoot, "failed"));
        Directory.CreateDirectory(Path.Combine(InstallerPaths.ProgramDataRoot, "quarantine"));
        Directory.CreateDirectory(Path.Combine(InstallerPaths.ProgramDataRoot, "logs"));

        if (!options.HandoffRoot.StartsWith(@"\\", StringComparison.Ordinal))
        {
            Directory.CreateDirectory(options.HandoffRoot);
        }

        log("Installing HealthMailer application files.");
        CopyDirectory(InstallerPaths.PayloadPublishRoot, InstallerPaths.ProgramFilesRoot);

        log("Writing HealthMailer configuration.");
        WriteConfig(options);

        log("Registering HealthMailer watcher task.");
        RegisterScheduledTask();
    }

    private static void WriteConfig(InstallOptions options)
    {
        object config = new
        {
            HandoffRoot = options.HandoffRoot,
            LocalRoot = InstallerPaths.ProgramDataRoot,
            PollIntervalSeconds = 5,
            StaleLockMinutes = 30,
            WriteHtmlSummary = false,
            ChartCopy = new
            {
                Enabled = false,
                DestinationRoot = string.Empty,
                FileNameTemplate = "Rx-{MRN}-{PackageId}.pdf",
                RequireMrn = true
            },
            Logging = new { MaxLogBytes = 10485760, MaxLogFiles = 5 },
            SendMail = true
        };

        string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(InstallerPaths.ConfigPath, json);
    }

    private static void RegisterScheduledTask()
    {
        string exe = InstallerPaths.InstalledExePath;
        string config = InstallerPaths.ConfigPath;
        string user = Environment.UserDomainName + "\\" + Environment.UserName;
        string command = @"
$action = New-ScheduledTaskAction -Execute '" + EscapeSingleQuoted(exe) + @"' -Argument '--watch --config """ + EscapeForPowerShellDoubleQuoted(config) + @"""'
$logonTrigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$watchdogTrigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 1) -RepetitionDuration (New-TimeSpan -Days 999)
$principal = New-ScheduledTaskPrincipal -UserId '" + EscapeSingleQuoted(user) + @"' -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit (New-TimeSpan -Days 999) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
Register-ScheduledTask -TaskName '" + InstallerPaths.TaskName + @"' -Action $action -Trigger @($logonTrigger, $watchdogTrigger) -Principal $principal -Settings $settings -Force | Out-Null
Start-ScheduledTask -TaskName '" + InstallerPaths.TaskName + @"'
";
        ProcessRunner.PowerShell(command);
    }

    private static string EscapeSingleQuoted(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string EscapeForPowerShellDoubleQuoted(string value)
    {
        return value.Replace("`", "``", StringComparison.Ordinal).Replace("\"", "`\"", StringComparison.Ordinal);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
