using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;

namespace HealthMailer;

public static class SecurityUtilities
{
    public static string ComputeSha256(string path)
    {
        using SHA256 sha = SHA256.Create();
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    public static bool LooksLikePdf(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        Span<byte> signature = stackalloc byte[5];
        using FileStream stream = File.OpenRead(path);
        return stream.Read(signature) == 5 &&
            signature[0] == (byte)'%' &&
            signature[1] == (byte)'P' &&
            signature[2] == (byte)'D' &&
            signature[3] == (byte)'F' &&
            signature[4] == (byte)'-';
    }

    public static string SanitizeFileComponent(string value)
    {
        string normalized = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            normalized = normalized.Replace(invalid, '_');
        }

        normalized = string.Join("_", normalized.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length > 80 ? normalized[..80] : normalized;
    }

    public static void TryHardenDropDirectory(string path)
    {
        if (IsUncPath(path))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(path);
            DirectoryInfo directory = new(path);
            DirectorySecurity security = new();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            SecurityIdentifier system = new(WellKnownSidType.LocalSystemSid, null);
            SecurityIdentifier admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
            SecurityIdentifier users = new(WellKnownSidType.BuiltinUsersSid, null);
            SecurityIdentifier owner = GetCurrentRuntimeUser();
            security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.Modify, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.Modify, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            directory.SetAccessControl(security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SystemException)
        {
        }
    }

    public static void TryHardenRuntimeDirectory(string path)
    {
        TryHardenRestrictedDirectory(path, FileSystemRights.Modify);
    }

    public static void TryHardenArchiveDirectory(string path)
    {
        TryHardenRestrictedDirectory(path, FileSystemRights.Modify);
    }

    public static void TryHardenConfigDirectory(string path)
    {
        TryHardenRestrictedDirectory(path, FileSystemRights.Modify);
    }

    public static void TryHardenLogDirectory(string path)
    {
        TryHardenRestrictedDirectory(path, FileSystemRights.Modify);
    }

    public static void TryHardenConfigFile(string path)
    {
        TryHardenRestrictedFile(path, FileSystemRights.ReadAndExecute);
    }

    public static void TryHardenLedgerFile(string path)
    {
        TryHardenRestrictedFile(path, FileSystemRights.Modify);
    }

    private static void TryHardenRestrictedDirectory(string path, FileSystemRights userRights)
    {
        if (IsUncPath(path))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(path);
            DirectoryInfo directory = new(path);
            DirectorySecurity security = new();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            SecurityIdentifier system = new(WellKnownSidType.LocalSystemSid, null);
            SecurityIdentifier admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
            SecurityIdentifier owner = GetCurrentRuntimeUser();
            security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(owner, userRights, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            directory.SetAccessControl(security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SystemException)
        {
        }
    }

    private static void TryHardenRestrictedFile(string path, FileSystemRights userRights)
    {
        if (IsUncPath(path) || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            FileInfo file = new(path);
            FileSecurity security = new();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            SecurityIdentifier system = new(WellKnownSidType.LocalSystemSid, null);
            SecurityIdentifier admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
            SecurityIdentifier owner = GetCurrentRuntimeUser();
            security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(owner, userRights, AccessControlType.Allow));
            file.SetAccessControl(security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SystemException)
        {
        }
    }

    private static bool IsUncPath(string path)
    {
        return string.IsNullOrWhiteSpace(path) || path.StartsWith(@"\\", StringComparison.Ordinal);
    }

    private static SecurityIdentifier GetCurrentRuntimeUser()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return identity.User ?? new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
    }
}
