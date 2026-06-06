using System.Security.AccessControl;
using System.Security.Principal;

namespace HealthMailerInstaller;

internal static class InstallerSecurity
{
    private const FileSystemRights UnsafeRights =
        FileSystemRights.WriteData |
        FileSystemRights.AppendData |
        FileSystemRights.WriteAttributes |
        FileSystemRights.WriteExtendedAttributes |
        FileSystemRights.Delete |
        FileSystemRights.DeleteSubdirectoriesAndFiles |
        FileSystemRights.ChangePermissions |
        FileSystemRights.TakeOwnership;

    public static void PrepareApplicationDirectoryForUpdate(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            DirectoryInfo directory = new(path);
            DirectorySecurity security = directory.GetAccessControl(AccessControlSections.Access);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            SecurityIdentifier runtimeUser = CurrentUserSid();
            security.SetAccessRule(DirectoryRule(runtimeUser, FileSystemRights.Modify));
            directory.SetAccessControl(security);

            string probePath = Path.Combine(path, ".healthmailer-update-write-probe-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(probePath, "HealthMailer installer update write probe.");
            }
            finally
            {
                File.Delete(probePath);
            }
        }
        catch (Exception ex) when (ex is not FatalSecurityException)
        {
            throw new FatalSecurityException(
                "Could not prepare HealthMailer application binary folder '" + path +
                "' for repair/update. Run HealthMailer repair/update as the owning sender user or with approved administrative support.",
                ex);
        }
    }

    public static void HardenApplicationDirectory(string path)
    {
        try
        {
            SecurityIdentifier runtimeUser = CurrentUserSid();
            DirectorySecurity security = new();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(DirectoryRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl));
            security.AddAccessRule(DirectoryRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl));
            security.AddAccessRule(DirectoryRule(runtimeUser, FileSystemRights.ReadAndExecute));
            new DirectoryInfo(path).SetAccessControl(security);
            VerifyApplicationDirectory(path);
        }
        catch (Exception ex) when (ex is not FatalSecurityException)
        {
            throw new FatalSecurityException("Could not harden and verify HealthMailer application binary folder '" + path + "'.", ex);
        }
    }

    public static void VerifyApplicationDirectory(string path)
    {
        try
        {
            DirectorySecurity security = new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access);
            if (!security.AreAccessRulesProtected)
            {
                throw new FatalSecurityException("HealthMailer application binary folder inherits permissions: " + path);
            }

            SecurityIdentifier system = new(WellKnownSidType.LocalSystemSid, null);
            SecurityIdentifier admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
            SecurityIdentifier runtimeUser = CurrentUserSid();
            AuthorizationRuleCollection rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));

            RequireRule(rules, system, FileSystemRights.FullControl, path);
            RequireRule(rules, admins, FileSystemRights.FullControl, path);
            RequireRule(rules, runtimeUser, FileSystemRights.ReadAndExecute, path);

            foreach (FileSystemAccessRule rule in rules.OfType<FileSystemAccessRule>())
            {
                if (rule.AccessControlType == AccessControlType.Allow &&
                    IsBroadOrdinaryGroup((SecurityIdentifier)rule.IdentityReference) &&
                    (rule.FileSystemRights & UnsafeRights) != 0)
                {
                    throw new FatalSecurityException("HealthMailer application binary folder grants ordinary users unsafe write/tamper rights: " + path);
                }
            }
        }
        catch (Exception ex) when (ex is not FatalSecurityException)
        {
            throw new FatalSecurityException("Could not verify HealthMailer application binary folder ACL '" + path + "'.", ex);
        }
    }

    private static FileSystemAccessRule DirectoryRule(SecurityIdentifier identity, FileSystemRights rights) =>
        new(identity, rights, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow);

    private static void RequireRule(AuthorizationRuleCollection rules, SecurityIdentifier identity, FileSystemRights rights, string path)
    {
        bool present = rules.OfType<FileSystemAccessRule>().Any(rule =>
            rule.AccessControlType == AccessControlType.Allow &&
            identity.Equals(rule.IdentityReference) &&
            (rule.FileSystemRights & rights) == rights);
        if (!present)
        {
            throw new FatalSecurityException("HealthMailer application binary folder is missing a required ACL for " + identity.Value + ": " + path);
        }
    }

    private static bool IsBroadOrdinaryGroup(SecurityIdentifier identity) =>
        identity.IsWellKnown(WellKnownSidType.BuiltinUsersSid) ||
        identity.IsWellKnown(WellKnownSidType.AuthenticatedUserSid) ||
        identity.IsWellKnown(WellKnownSidType.WorldSid);

    private static SecurityIdentifier CurrentUserSid()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return identity.User ?? throw new FatalSecurityException("Could not determine the intended HealthMailer runtime user SID.");
    }
}

internal sealed class FatalSecurityException : Exception
{
    public FatalSecurityException(string message) : base(message) { }
    public FatalSecurityException(string message, Exception innerException) : base(message, innerException) { }
}
