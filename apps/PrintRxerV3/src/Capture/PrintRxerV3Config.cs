using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.AccessControl;
using System.Security.Principal;

namespace PrintRxerV3.Capture;

public sealed class PrintRxerV3Config
{
    public string IncomingRoot { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "printRxer", "work", "incoming");
    public string ProcessedRoot { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "printRxer", "processed");
    public string DeferredRoot { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "printRxer", "deferred");
    public string LocalOutboxRoot { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "printRxer", "pending-outbox");
    public string PublishedRoot { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "printRxer", "published");
    public string FailedRoot { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "printRxer", "failed");
    public string LogsRoot { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "printRxer", "logs");
    public string TempRoot { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "printRxer", "temp");
    public string HandoffRoot { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "printRxer", "handoff");
    public int PayloadStableSeconds { get; set; } = 1;
    public int MetadataGraceSeconds { get; set; } = 60;
    public bool RequireJobOwnerMatch { get; set; } = true;
    public bool AllowMissingSubmittingSid { get; set; }
    public int RetryIntervalSeconds { get; set; } = 1;
    public int MaxLogBytes { get; set; } = 5 * 1024 * 1024;
    public int MaxLogFiles { get; set; } = 3;
    public RecipientSourceSettings RecipientSource { get; set; } = new();

    [JsonIgnore]
    public string ConfigPath => DefaultConfigPath;

    public static string DefaultConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "printRxer", "config", "printRxer.settings.json");

    public static PrintRxerV3Config Load(string? path = null)
    {
        string configPath = string.IsNullOrWhiteSpace(path) ? DefaultConfigPath : path;
        if (!File.Exists(configPath))
        {
            PrintRxerV3Config created = new();
            created.Normalize();
            created.EnsureLocalDirectories();
            created.Save(configPath);
            return created;
        }

        JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
        PrintRxerV3Config config = JsonSerializer.Deserialize<PrintRxerV3Config>(File.ReadAllText(configPath), options) ?? new PrintRxerV3Config();
        config.Normalize();
        config.EnsureLocalDirectories();
        return config;
    }

    public void Save(string? path = null)
    {
        string configPath = string.IsNullOrWhiteSpace(path) ? DefaultConfigPath : path;
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        JsonSerializerOptions options = new() { WriteIndented = true };
        File.WriteAllText(configPath, JsonSerializer.Serialize(this, options));
        TryHardenDirectory(Path.GetDirectoryName(configPath)!, FileSystemRights.Modify);
        TryHardenFile(configPath, FileSystemRights.ReadAndExecute);
    }

    public void EnsureLocalDirectories()
    {
        Directory.CreateDirectory(IncomingRoot);
        Directory.CreateDirectory(ProcessedRoot);
        Directory.CreateDirectory(DeferredRoot);
        Directory.CreateDirectory(LocalOutboxRoot);
        Directory.CreateDirectory(PublishedRoot);
        Directory.CreateDirectory(FailedRoot);
        Directory.CreateDirectory(LogsRoot);
        Directory.CreateDirectory(TempRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        string recipientRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "printRxer", "data", "recipients");
        Directory.CreateDirectory(recipientRoot);
        TryHardenDirectory(ProcessedRoot, FileSystemRights.Modify);
        TryHardenDirectory(DeferredRoot, FileSystemRights.Modify);
        TryHardenDirectory(LocalOutboxRoot, FileSystemRights.Modify);
        TryHardenDirectory(PublishedRoot, FileSystemRights.Modify);
        TryHardenDirectory(FailedRoot, FileSystemRights.Modify);
        TryHardenDirectory(LogsRoot, FileSystemRights.Modify);
        TryHardenDirectory(TempRoot, FileSystemRights.Modify);
        TryHardenDirectory(recipientRoot, FileSystemRights.Modify);
        TryHardenFile(Path.Combine(recipientRoot, "recipients.cache.csv"), FileSystemRights.Modify);
        TryHardenFile(Path.Combine(recipientRoot, "recipient-source-status.json"), FileSystemRights.Modify);
        TryHardenFile(Path.Combine(recipientRoot, "bundled-recipients.csv"), FileSystemRights.ReadAndExecute);
        TryHardenDirectory(Path.GetDirectoryName(ConfigPath)!, FileSystemRights.Modify);
        if (!IsUncPath(HandoffRoot))
        {
            Directory.CreateDirectory(HandoffRoot);
            TryHardenDropDirectory(HandoffRoot);
        }
    }

    public void Normalize()
    {
        if (PayloadStableSeconds <= 0)
        {
            PayloadStableSeconds = 1;
        }

        if (MetadataGraceSeconds <= 0)
        {
            MetadataGraceSeconds = 60;
        }

        if (RetryIntervalSeconds <= 0)
        {
            RetryIntervalSeconds = 1;
        }

        if (string.IsNullOrWhiteSpace(TempRoot))
        {
            TempRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "printRxer", "temp");
        }

        if (MaxLogBytes <= 0)
        {
            MaxLogBytes = 5 * 1024 * 1024;
        }

        if (MaxLogFiles < 0)
        {
            MaxLogFiles = 3;
        }

        RecipientSource ??= new RecipientSourceSettings();
        RecipientSource.Normalize();
    }

    private static void TryHardenDropDirectory(string path)
    {
        if (IsUncPath(path))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(path);
            DirectoryInfo directory = new(path);
            DirectorySecurity security = CreateBaseDirectorySecurity(FileSystemRights.Modify);
            SecurityIdentifier users = new(WellKnownSidType.BuiltinUsersSid, null);
            security.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.Modify, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            directory.SetAccessControl(security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SystemException)
        {
        }
    }

    private static void TryHardenDirectory(string path, FileSystemRights runtimeRights)
    {
        if (IsUncPath(path))
        {
            return;
        }

        try
        {
            DirectoryInfo directory = new(path);
            directory.SetAccessControl(CreateBaseDirectorySecurity(runtimeRights));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SystemException)
        {
        }
    }

    private static void TryHardenFile(string path, FileSystemRights runtimeRights)
    {
        if (IsUncPath(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            FileSecurity security = new();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            SecurityIdentifier system = new(WellKnownSidType.LocalSystemSid, null);
            SecurityIdentifier admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            SecurityIdentifier runtimeUser = identity.User ?? system;
            security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(runtimeUser, runtimeRights, AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SystemException)
        {
        }
    }

    private static DirectorySecurity CreateBaseDirectorySecurity(FileSystemRights runtimeRights)
    {
        DirectorySecurity security = new();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        SecurityIdentifier system = new(WellKnownSidType.LocalSystemSid, null);
        SecurityIdentifier admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier runtimeUser = identity.User ?? system;
        security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(runtimeUser, runtimeRights, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }

    private static bool IsUncPath(string path)
    {
        return string.IsNullOrWhiteSpace(path) || path.StartsWith(@"\\", StringComparison.Ordinal);
    }
}

public sealed class RecipientSourceSettings
{
    public string Mode { get; set; } = "HandoffDerivedWithFallback";
    public string CentralRelativePath { get; set; } = "recipients";
    public string CentralFileName { get; set; } = "recipients.csv";
    public bool UseBundledFallback { get; set; } = true;
    public bool RefreshOnStartup { get; set; } = true;
    public int StartupRefreshDelaySeconds { get; set; } = 20;
    public int RefreshIntervalHours { get; set; } = 12;
    public int MaxCacheAgeDaysWarning { get; set; } = 30;
    public int MaxCacheAgeDaysBlock { get; set; } = 365;

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Mode))
        {
            Mode = "HandoffDerivedWithFallback";
        }

        if (string.IsNullOrWhiteSpace(CentralRelativePath))
        {
            CentralRelativePath = "recipients";
        }

        if (string.IsNullOrWhiteSpace(CentralFileName))
        {
            CentralFileName = "recipients.csv";
        }

        if (StartupRefreshDelaySeconds <= 0)
        {
            StartupRefreshDelaySeconds = 20;
        }

        if (RefreshIntervalHours <= 0)
        {
            RefreshIntervalHours = 12;
        }

        if (MaxCacheAgeDaysWarning <= 0)
        {
            MaxCacheAgeDaysWarning = 30;
        }

        if (MaxCacheAgeDaysBlock <= 0)
        {
            MaxCacheAgeDaysBlock = 365;
        }
    }
}
