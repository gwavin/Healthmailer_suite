namespace HealthMailer;

public static class AttachmentDisplayName
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string Sanitize(string value, string documentKind = "ClinicalDocument")
    {
        string fallback = string.Equals(documentKind, "Prescription", StringComparison.OrdinalIgnoreCase)
            ? "prescription_" + DateTimeOffset.Now.ToString("yyyyMMdd_HHmm") + ".pdf"
            : "clinicalDocument_" + DateTimeOffset.Now.ToString("yyyyMMdd_HHmm") + ".pdf";

        string fileName = Path.GetFileName((value ?? string.Empty).Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
        string sanitized = new(fileName.Where(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-' or '.').ToArray());
        sanitized = sanitized.Trim('.', ' ', '_');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = fallback;
        }

        string stem = Path.GetFileNameWithoutExtension(sanitized).Trim('.', ' ', '_');
        if (string.IsNullOrWhiteSpace(stem) || ReservedNames.Contains(stem))
        {
            sanitized = fallback;
            stem = Path.GetFileNameWithoutExtension(sanitized).Trim('.', ' ', '_');
        }

        sanitized = stem + ".pdf";
        if (sanitized.Length > 120)
        {
            sanitized = sanitized[..116].Trim('.', ' ', '_') + ".pdf";
        }

        return sanitized;
    }
}

public sealed class PreparedAttachment : IDisposable
{
    private readonly string _directory;
    private bool _disposed;

    public PreparedAttachment(string path, string directory)
    {
        Path = path;
        _directory = directory;
    }

    public string Path { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public static class AttachmentFilePreparer
{
    public static PreparedAttachment Prepare(DeliveryPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        string safePackageId = AttachmentDisplayName.Sanitize(package.PackageId, package.DocumentKind);
        safePackageId = System.IO.Path.GetFileNameWithoutExtension(safePackageId);
        string baseDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HealthMailer");
        string directory = System.IO.Path.Combine(baseDirectory, safePackageId + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);

        string fileName = AttachmentDisplayName.Sanitize(package.AttachmentDisplayName, package.DocumentKind);
        string destination = System.IO.Path.Combine(directory, fileName);
        string fullDirectory = System.IO.Path.GetFullPath(directory);
        string fullDestination = System.IO.Path.GetFullPath(destination);
        if (!fullDestination.StartsWith(fullDirectory + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Attachment display filename resolved outside the temporary attachment folder.");
        }

        File.Copy(package.AttachmentPath, fullDestination, overwrite: false);
        return new PreparedAttachment(fullDestination, fullDirectory);
    }
}
