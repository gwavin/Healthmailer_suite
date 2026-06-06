using System.Text.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using PrintRxerV3.Recipients;

namespace PrintRxerV3Installer;

public class FatalSecurityException : Exception
{
    public FatalSecurityException(string message) : base(message) { }
    public FatalSecurityException(string message, Exception innerException) : base(message, innerException) { }
}

internal static class PrintRxerInstaller
{
    public static void Install(InstallOptions options, Action<string> log)
    {
        if (!Directory.Exists(InstallerPaths.PayloadPublishRoot))
        {
            throw new DirectoryNotFoundException("The installer payload is missing: " + InstallerPaths.PayloadPublishRoot);
        }

        log("Creating local folders.");
        CreateDirectories();

        log("Stopping existing printRxer watcher/process before updating application files.");
        StopExistingWatcher(log);

        log("Installing printRxer application files.");
        CopyDirectory(InstallerPaths.PayloadPublishRoot, InstallerPaths.ProgramFilesRoot);

        log("Seeding recipients and picker image if missing.");
        SeedDataFiles();
        HardenRecipientFiles();

        log("Writing printRxer configuration.");
        WriteConfig(options.HandoffRoot);

        log("Preparing central recipient list if available.");
        PrepareCentralRecipients(options.HandoffRoot, log);

        log("Installing printRxer capture printer.");
        InstallCapturePrinter();

        log("Hardening SYSTEM-loaded port monitor.");
        HardenAndVerifyPortMonitor();

        log("Verifying printRxer capture printer.");
        VerifyCapturePrinter();

        log("Registering printRxer watcher task.");
        log("printRxer scheduled task target: all interactive Windows users.");
        log("printRxer installer Windows identity: " + WindowsIdentity.GetCurrent().Name);
        RegisterScheduledTask();
    }

    private static void CreateDirectories()
    {
        foreach (string path in new[]
        {
            InstallerPaths.ProgramFilesRoot,
            InstallerPaths.ProgramDataRoot,
            Path.Combine(InstallerPaths.ProgramDataRoot, "config"),
            Path.Combine(InstallerPaths.ProgramDataRoot, "data", "recipients"),
            Path.Combine(InstallerPaths.ProgramDataRoot, "data", "Images"),
            Path.Combine(InstallerPaths.ProgramDataRoot, "work", "incoming"),
            Path.Combine(InstallerPaths.ProgramDataRoot, "processed"),
            Path.Combine(InstallerPaths.ProgramDataRoot, "deferred"),
            Path.Combine(InstallerPaths.ProgramDataRoot, "pending-outbox"),
            Path.Combine(InstallerPaths.ProgramDataRoot, "published"),
            Path.Combine(InstallerPaths.ProgramDataRoot, "failed"),
            Path.Combine(InstallerPaths.ProgramDataRoot, "logs"),
            Path.Combine(InstallerPaths.ProgramDataRoot, "temp")
        })
        {
            Directory.CreateDirectory(path);
        }
    }

    private static void SeedDataFiles()
    {
        string recipientsSource = Path.Combine(InstallerPaths.PayloadAssetsRoot, "recipients", "recipients.csv");
        string imageSource = Path.Combine(InstallerPaths.PayloadAssetsRoot, "branding", "mncms_400x400.jpg");
        string recipientsDestination = Path.Combine(InstallerPaths.ProgramDataRoot, "data", "recipients", "bundled-recipients.csv");
        string imageDestination = Path.Combine(InstallerPaths.ProgramDataRoot, "data", "Images", "mncms_400x400.jpg");

        if (File.Exists(recipientsSource))
        {
            File.Copy(recipientsSource, recipientsDestination, overwrite: true);
        }

        if (File.Exists(imageSource) && !File.Exists(imageDestination))
        {
            File.Copy(imageSource, imageDestination);
        }
    }

    private static void PrepareCentralRecipients(string handoffRoot, Action<string> log)
    {
        if (string.IsNullOrWhiteSpace(handoffRoot))
        {
            return;
        }

        string bundled = Path.Combine(InstallerPaths.ProgramDataRoot, "data", "recipients", "bundled-recipients.csv");
        if (!File.Exists(bundled))
        {
            log("RecipientCentralSeedSkipped: bundled fallback recipient file was not installed.");
            return;
        }

        string centralFolder = Path.Combine(handoffRoot, "recipients");
        string centralFile = Path.Combine(centralFolder, "recipients.csv");
        string cacheDestination = Path.Combine(InstallerPaths.ProgramDataRoot, "data", "recipients", "recipients.cache.csv");
        try
        {
            log("RecipientCentralFolderCreateAttempted: " + centralFolder);
            Directory.CreateDirectory(centralFolder);
            log("RecipientCentralFolderCreated: " + centralFolder);

            if (!File.Exists(centralFile))
            {
                File.Copy(bundled, centralFile);
                log("RecipientCentralSeededFromBundled: " + centralFile);
            }
            else
            {
                log("RecipientCentralAlreadyExists: " + centralFile);
            }

            _ = RecipientCsvValidator.LoadValidated(centralFile);
            File.Copy(centralFile, cacheDestination, overwrite: true);
            TryHardenFile(cacheDestination, FileSystemRights.Modify);
            log("RecipientCacheUpdated: " + cacheDestination);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidDataException or SystemException)
        {
            log("RecipientCentralFolderCreateFailed: " + ex.Message);
        }
    }

    private static void WriteConfig(string handoffRoot)
    {
        object config = new
        {
            IncomingRoot = Path.Combine(InstallerPaths.ProgramDataRoot, "work", "incoming"),
            ProcessedRoot = Path.Combine(InstallerPaths.ProgramDataRoot, "processed"),
            DeferredRoot = Path.Combine(InstallerPaths.ProgramDataRoot, "deferred"),
            LocalOutboxRoot = Path.Combine(InstallerPaths.ProgramDataRoot, "pending-outbox"),
            PublishedRoot = Path.Combine(InstallerPaths.ProgramDataRoot, "published"),
            FailedRoot = Path.Combine(InstallerPaths.ProgramDataRoot, "failed"),
            LogsRoot = Path.Combine(InstallerPaths.ProgramDataRoot, "logs"),
            TempRoot = Path.Combine(InstallerPaths.ProgramDataRoot, "temp"),
            HandoffRoot = handoffRoot,
            PayloadStableSeconds = 1,
            RequireJobOwnerMatch = true,
            AllowMissingSubmittingSid = false,
            RetryIntervalSeconds = 1,
            MaxLogBytes = 5242880,
            MaxLogFiles = 3,
            RecipientSource = new
            {
                Mode = "HandoffDerivedWithFallback",
                CentralRelativePath = "recipients",
                CentralFileName = "recipients.csv",
                UseBundledFallback = true,
                RefreshOnStartup = true,
                StartupRefreshDelaySeconds = 20,
                RefreshIntervalHours = 12,
                MaxCacheAgeDaysWarning = 30,
                MaxCacheAgeDaysBlock = 365
            }
        };

        string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(InstallerPaths.ConfigPath, json);
    }

    private static void HardenRecipientFiles()
    {
        string recipientRoot = Path.Combine(InstallerPaths.ProgramDataRoot, "data", "recipients");
        TryHardenDirectory(recipientRoot, FileSystemRights.Modify);
        TryHardenFile(Path.Combine(recipientRoot, "bundled-recipients.csv"), FileSystemRights.ReadAndExecute);
        TryHardenFile(Path.Combine(recipientRoot, "recipients.cache.csv"), FileSystemRights.Modify);
        TryHardenFile(Path.Combine(recipientRoot, "recipient-source-status.json"), FileSystemRights.Modify);
    }

    private static void TryHardenDirectory(string path, FileSystemRights runtimeRights)
    {
        try
        {
            DirectoryInfo directory = new(path);
            directory.SetAccessControl(CreateBaseDirectorySecurity(runtimeRights));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new FatalSecurityException($"Failed to secure/harden directory ACLs on {path}", ex);
        }
    }

    private static void TryHardenFile(string path, FileSystemRights runtimeRights)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            FileSecurity security = new();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            SecurityIdentifier system = new(WellKnownSidType.LocalSystemSid, null);
            SecurityIdentifier admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
            SecurityIdentifier users = new(WellKnownSidType.BuiltinUsersSid, null);
            security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(users, runtimeRights, AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new FatalSecurityException($"Failed to secure/harden file ACLs on {path}", ex);
        }
    }

    private static DirectorySecurity CreateBaseDirectorySecurity(FileSystemRights runtimeRights)
    {
        DirectorySecurity security = new();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        SecurityIdentifier system = new(WellKnownSidType.LocalSystemSid, null);
        SecurityIdentifier admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
        SecurityIdentifier users = new(WellKnownSidType.BuiltinUsersSid, null);
        security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(users, runtimeRights, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }

    private static void RegisterScheduledTask()
    {
        string exe = InstallerPaths.InstalledExePath;
        string config = InstallerPaths.ConfigPath;
        string command = @"
$action = New-ScheduledTaskAction -Execute '" + EscapeSingleQuoted(exe) + @"' -Argument '--watch --config """ + EscapeForPowerShellDoubleQuoted(config) + @"""'
$logonTrigger = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal -GroupId 'BUILTIN\Users' -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -MultipleInstances Parallel -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit (New-TimeSpan -Days 999) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
Register-ScheduledTask -TaskName '" + InstallerPaths.TaskName + @"' -Action $action -Trigger $logonTrigger -Principal $principal -Settings $settings -Force | Out-Null
Start-ScheduledTask -TaskName '" + InstallerPaths.TaskName + @"'
";
        ProcessRunner.PowerShell(command);
    }

    private static void StopExistingWatcher(Action<string> log)
    {
        string command = @"
$taskNames = @('printRxer', 'PrintRxerV3', 'PrintRxer Agent')
foreach ($taskName in $taskNames) {
    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($task) {
        Write-Output ('Disabling scheduled task before install: ' + $taskName)
        Disable-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue | Out-Null
        Write-Output ('Stopping scheduled task before install: ' + $taskName)
        Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    } else {
        Write-Output ('Scheduled task not present before install: ' + $taskName)
    }
}
$processes = Get-Process -Name 'printRxer' -ErrorAction SilentlyContinue
if ($processes) {
    Write-Output 'Stopping running printRxer process before install.'
    $processes | Stop-Process -Force
} else {
    Write-Output 'No running printRxer process found before install.'
}
$agentProcesses = Get-Process -Name 'PrintRxer.Agent' -ErrorAction SilentlyContinue
if ($agentProcesses) {
    Write-Output 'Stopping running PrintRxer.Agent process before install.'
    $agentProcesses | Stop-Process -Force
} else {
    Write-Output 'No running PrintRxer.Agent process found before install.'
}
function WaitForStoppedProcesses {
    param([string[]] $Names)
    foreach ($name in $Names) {
        $deadline = (Get-Date).AddSeconds(10)
        while ((Get-Process -Name $name -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 250
        }
        if (Get-Process -Name $name -ErrorAction SilentlyContinue) {
            if ($name -eq 'printRxer') {
                throw 'printRxer process did not stop before install.'
            }
            throw 'PrintRxer.Agent process did not stop before install.'
        }
    }
}
WaitForStoppedProcesses @('printRxer', 'PrintRxer.Agent')
";
        string output = ProcessRunner.PowerShell(command);
        if (!string.IsNullOrWhiteSpace(output))
        {
            foreach (string line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            {
                log(line);
            }
        }
    }

    private static void InstallCapturePrinter()
    {
        string script = Path.Combine(InstallerPaths.PayloadToolsRoot, "Install-PrintRxerCapturePrinter.ps1");
        if (!File.Exists(script))
        {
            throw new FileNotFoundException("The capture printer install payload is missing.", script);
        }

        ProcessRunner.PowerShellFile(script);
    }

    private static void VerifyCapturePrinter()
    {
        string command = @"
$printer = Get-Printer -Name 'printRxer' -ErrorAction SilentlyContinue
$port = Get-PrinterPort -Name 'printrx:' -ErrorAction SilentlyContinue
$driver = Get-PrinterDriver -Name 'PrintRxer XPS Driver' -ErrorAction SilentlyContinue
if (-not $printer) { throw 'The printRxer printer is not visible in Windows after installation.' }
if (-not $port) { throw 'The printrx: capture port is not visible in Windows after installation.' }
if (-not $driver) { throw 'The PrintRxer XPS Driver is not visible in Windows after installation.' }
if ($printer.PortName -ne 'printrx:') { throw ('The printRxer printer is using port ' + $printer.PortName + ' instead of printrx:.') }
if ($printer.DriverName -ne 'PrintRxer XPS Driver') { throw ('The printRxer printer is using driver ' + $printer.DriverName + ' instead of PrintRxer XPS Driver.') }
";
        ProcessRunner.PowerShell(command);
    }

    private static void HardenAndVerifyPortMonitor()
    {
        string system32 = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32"));
        string dllPath = Path.GetFullPath(Path.Combine(system32, "PrintRxerPortMonitor.dll"));
        if (!string.Equals(Path.GetDirectoryName(dllPath), system32, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The port monitor DLL must be installed directly under Windows System32.");
        }

        if (!File.Exists(dllPath))
        {
            throw new FileNotFoundException("The installed port monitor DLL was not found.", dllPath);
        }

        SecurityIdentifier system = new(WellKnownSidType.LocalSystemSid, null);
        SecurityIdentifier admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
        SecurityIdentifier localService = new(WellKnownSidType.LocalServiceSid, null);
        SecurityIdentifier spoolerService = (SecurityIdentifier)new NTAccount(@"RESTRICTED SERVICES\PrintSpoolerService").Translate(typeof(SecurityIdentifier));

        FileSecurity fileSecurity = new();
        fileSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        fileSecurity.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, AccessControlType.Allow));
        fileSecurity.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, AccessControlType.Allow));
        fileSecurity.AddAccessRule(new FileSystemAccessRule(localService, FileSystemRights.ReadAndExecute | FileSystemRights.Read, AccessControlType.Allow));
        fileSecurity.AddAccessRule(new FileSystemAccessRule(spoolerService, FileSystemRights.ReadAndExecute | FileSystemRights.Read, AccessControlType.Allow));
        
        try
        {
            new FileInfo(dllPath).SetAccessControl(fileSecurity);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new FatalSecurityException($"Failed to secure/harden port monitor DLL ACLs on {dllPath}", ex);
        }
        VerifyPortMonitorFileSecurity(dllPath, system, admins, localService, spoolerService);

        const string monitorKeyPath = @"SYSTEM\CurrentControlSet\Control\Print\Monitors\PrintRxer Port Monitor";
        using RegistryKey key = Registry.LocalMachine.OpenSubKey(
            monitorKeyPath,
            RegistryKeyPermissionCheck.ReadWriteSubTree,
            RegistryRights.ChangePermissions | RegistryRights.ReadKey | RegistryRights.WriteKey)
            ?? throw new InvalidOperationException("The registered PrintRxer port monitor key was not found.");

        RegistrySecurity registrySecurity = new();
        registrySecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        registrySecurity.AddAccessRule(new RegistryAccessRule(system, RegistryRights.FullControl, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
        registrySecurity.AddAccessRule(new RegistryAccessRule(admins, RegistryRights.FullControl, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
        registrySecurity.AddAccessRule(new RegistryAccessRule(spoolerService, RegistryRights.ReadKey, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
        
        SecurityIdentifier users = new(WellKnownSidType.BuiltinUsersSid, null);
        registrySecurity.AddAccessRule(new RegistryAccessRule(users, RegistryRights.ReadKey, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
        
        try
        {
            key.SetAccessControl(registrySecurity);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            throw new FatalSecurityException("Failed to secure/harden port monitor registry key ACLs.", ex);
        }
        VerifyPortMonitorRegistrySecurity(key, system, admins, spoolerService);
    }

    private static void VerifyPortMonitorFileSecurity(string path, SecurityIdentifier system, SecurityIdentifier admins, SecurityIdentifier localService, SecurityIdentifier spoolerService)
    {
        FileSecurity security = new FileInfo(path).GetAccessControl(AccessControlSections.Access);
        if (!security.AreAccessRulesProtected)
        {
            throw new InvalidOperationException("Port monitor DLL ACL inheritance is still enabled.");
        }

        FileSystemAccessRule[] rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        VerifyOnlyAllowedFileRule(rules, system, FileSystemRights.FullControl);
        VerifyOnlyAllowedFileRule(rules, admins, FileSystemRights.FullControl);
        VerifyOnlyAllowedFileRule(rules, localService, FileSystemRights.ReadAndExecute | FileSystemRights.Read);
        VerifyOnlyAllowedFileRule(rules, spoolerService, FileSystemRights.ReadAndExecute | FileSystemRights.Read);
        if (rules.Length != 4)
        {
            throw new InvalidOperationException("Port monitor DLL contains an unexpected explicit access rule.");
        }
    }

    private static void VerifyOnlyAllowedFileRule(FileSystemAccessRule[] rules, SecurityIdentifier identity, FileSystemRights requiredRights)
    {
        FileSystemAccessRule? rule = rules.SingleOrDefault(candidate => identity.Equals(candidate.IdentityReference));
        if (rule is null || rule.AccessControlType != AccessControlType.Allow || (rule.FileSystemRights & requiredRights) != requiredRights)
        {
            throw new InvalidOperationException("Port monitor DLL ACL does not match the hardened security specification.");
        }
    }

    private static void VerifyPortMonitorRegistrySecurity(RegistryKey key, SecurityIdentifier system, SecurityIdentifier admins, SecurityIdentifier spoolerService)
    {
        RegistrySecurity security = key.GetAccessControl(AccessControlSections.Access);
        if (!security.AreAccessRulesProtected)
        {
            throw new InvalidOperationException("Port monitor registry-key ACL inheritance is still enabled.");
        }

        RegistryAccessRule[] rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<RegistryAccessRule>()
            .ToArray();
        foreach (SecurityIdentifier identity in new[] { system, admins })
        {
            RegistryAccessRule? rule = rules.SingleOrDefault(candidate => identity.Equals(candidate.IdentityReference));
            if (rule is null || rule.AccessControlType != AccessControlType.Allow || (rule.RegistryRights & RegistryRights.FullControl) != RegistryRights.FullControl)
            {
                throw new InvalidOperationException("Port monitor registry-key ACL does not match the hardened security specification.");
            }
        }

        RegistryAccessRule? spoolerRule = rules.SingleOrDefault(candidate => spoolerService.Equals(candidate.IdentityReference));
        if (spoolerRule is null || spoolerRule.AccessControlType != AccessControlType.Allow || (spoolerRule.RegistryRights & RegistryRights.ReadKey) != RegistryRights.ReadKey)
        {
            throw new InvalidOperationException("Port monitor registry-key ACL does not grant the spooler service read-only access.");
        }

        SecurityIdentifier users = new(WellKnownSidType.BuiltinUsersSid, null);
        RegistryAccessRule? usersRule = rules.SingleOrDefault(candidate => users.Equals(candidate.IdentityReference));
        if (usersRule is null || usersRule.AccessControlType != AccessControlType.Allow || (usersRule.RegistryRights & RegistryRights.ReadKey) != RegistryRights.ReadKey)
        {
            throw new InvalidOperationException("Port monitor registry-key ACL does not grant the Users group read-only access.");
        }

        if (rules.Length != 4)
        {
            throw new InvalidOperationException("Port monitor registry key contains an unexpected explicit access rule.");
        }
    }

    private static string EscapeSingleQuoted(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

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
