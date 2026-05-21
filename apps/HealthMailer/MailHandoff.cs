using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace HealthMailer;

public interface IMailHandoff
{
    void Send(DeliveryPackage package);
}

public sealed class OutlookMailHandoff : IMailHandoff
{
    private static readonly Guid OutlookApplicationClsid = new("0006F03A-0000-0000-C000-000000000046");

    public void Send(DeliveryPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        object? outlook = null;
        object? mailItem = null;
        object? attachments = null;

        try
        {
            outlook = CreateVerifiedOutlookApplication();
            EnsureSessionLoggedOn(outlook);
            mailItem = InvokeMethod(outlook, "CreateItem", 0);

            SetProperty(mailItem, "To", package.RecipientEmail);
            SetProperty(mailItem, "Subject", package.Subject);
            SetProperty(mailItem, "Body", BuildBody(package));
            ResolveRecipients(mailItem);

            attachments = GetProperty(mailItem, "Attachments");
            InvokeMethod(attachments, "Add", package.AttachmentPath);

            InvokeMethod(mailItem, "Save");
            InvokeMethod(mailItem, "Send");
        }
        finally
        {
            ReleaseComObject(attachments);
            ReleaseComObject(mailItem);
            ReleaseComObject(outlook);
        }
    }

    public static string ValidateOutlookRegistration()
    {
        string registryPath = @"CLSID\{" + OutlookApplicationClsid.ToString().ToUpperInvariant() + @"}\LocalServer32";
        using RegistryKey? key = Registry.ClassesRoot.OpenSubKey(registryPath);
        string? command = Convert.ToString(key?.GetValue(null), CultureInfo.InvariantCulture);
        string executablePath = ExtractExecutablePath(command);
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("The Outlook COM registration could not be resolved.");
        }

        string fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(executablePath));
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The Outlook COM server path was not found.", fullPath);
        }

        if (!string.Equals(Path.GetFileName(fullPath), "OUTLOOK.EXE", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The Outlook COM registration does not point to OUTLOOK.EXE.");
        }

        if (!IsUnderProgramFiles(fullPath))
        {
            throw new InvalidOperationException("The Outlook COM registration does not point to a trusted Program Files location.");
        }

        return fullPath;
    }

    private static string BuildBody(DeliveryPackage package)
    {
        return (package.Body ?? string.Empty).TrimEnd() + $@"

Operational provenance
----------------------
Source application: PrintRxer_v3
Courier application: HealthMailer
Delivery mode: Local Outlook COM handoff
Package ID: {package.PackageId}
Attachment SHA256: {package.PdfSha256}
Machine: {Environment.MachineName}
Windows user: {Environment.UserName}
";
    }

    private static object CreateVerifiedOutlookApplication()
    {
        ValidateOutlookRegistration();
        Type outlookType = Type.GetTypeFromCLSID(OutlookApplicationClsid, throwOnError: true)!;
        return Activator.CreateInstance(outlookType)!;
    }

    private static void EnsureSessionLoggedOn(object outlook)
    {
        object? session = null;
        try
        {
            session = InvokeMethod(outlook, "GetNamespace", "MAPI");
            InvokeMethod(session, "Logon", Type.Missing, Type.Missing, false, false);
        }
        finally
        {
            ReleaseComObject(session);
        }
    }

    private static void ResolveRecipients(object mailItem)
    {
        object? recipients = null;
        try
        {
            recipients = GetProperty(mailItem, "Recipients");
            InvokeMethod(recipients, "ResolveAll");
        }
        finally
        {
            ReleaseComObject(recipients);
        }
    }

    private static string ExtractExecutablePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return string.Empty;
        }

        string trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            int endQuote = trimmed.IndexOf('"', 1);
            if (endQuote > 1)
            {
                return trimmed[1..endQuote];
            }
        }

        int executableIndex = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return executableIndex > 0 ? trimmed[..(executableIndex + 4)] : trimmed.Split(' ')[0];
    }

    private static bool IsUnderProgramFiles(string path)
    {
        string fullPath = Path.GetFullPath(path);
        return StartsWithDirectory(fullPath, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)) ||
            StartsWithDirectory(fullPath, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
    }

    private static bool StartsWithDirectory(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        string normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static object InvokeMethod(object target, string name, params object[] arguments)
    {
        try
        {
            return target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, arguments, CultureInfo.InvariantCulture)!;
        }
        catch (TargetInvocationException ex)
        {
            throw CreateInvocationException("method", name, ex);
        }
    }

    private static object GetProperty(object target, string name)
    {
        try
        {
            return target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null, CultureInfo.InvariantCulture)!;
        }
        catch (TargetInvocationException ex)
        {
            throw CreateInvocationException("property", name, ex);
        }
    }

    private static void SetProperty(object target, string name, object value)
    {
        try
        {
            target.GetType().InvokeMember(name, BindingFlags.SetProperty, null, target, [value], CultureInfo.InvariantCulture);
        }
        catch (TargetInvocationException ex)
        {
            throw CreateInvocationException("property", name, ex);
        }
    }

    private static Exception CreateInvocationException(string memberType, string memberName, TargetInvocationException ex)
    {
        Exception inner = ex;
        while (inner is TargetInvocationException && inner.InnerException is not null)
        {
            inner = inner.InnerException;
        }

        return new InvalidOperationException($"Outlook COM {memberType} '{memberName}' failed: {inner.Message}", inner);
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}
