using System.Text.RegularExpressions;
using PrintRxerV3.Capture;

namespace PrintRxerV3.Metadata;

public enum DocumentKind
{
    Prescription,
    ClinicalDocument
}

public sealed record DocumentMessageDefaults
{
    public required DocumentKind DocumentKind { get; init; }
    public required string DocumentName { get; init; }
    public required string Subject { get; init; }
    public required string Body { get; init; }
    public required string AttachmentDisplayName { get; init; }
}

public static class DocumentDefaults
{
    private static readonly string[] PrescriptionTerms = ["rx", "prescription", "medication", "medicine", "drug", "pharmacy", "dispense"];
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static DocumentKind InferKind(CapturedPrintJobContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        string haystack = string.Join(' ', context.DocumentName, context.PrinterName, context.PrescribedBy);
        return PrescriptionTerms.Any(term => Regex.IsMatch(haystack, @"\b" + Regex.Escape(term) + @"\b", RegexOptions.IgnoreCase))
            ? DocumentKind.Prescription
            : DocumentKind.ClinicalDocument;
    }

    public static DocumentMessageDefaults Create(DocumentKind kind, CapturedPrintJobContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        string documentName = kind == DocumentKind.Prescription ? "Prescription" : "Clinical document";
        string subject = documentName;
        string body = "Hello," + Environment.NewLine + Environment.NewLine +
            (kind == DocumentKind.Prescription
                ? "Please see the attached prescription."
                : "Please see the attached clinical document.") +
            Environment.NewLine + Environment.NewLine +
            "Document: " + documentName + Environment.NewLine + Environment.NewLine +
            "Kind regards,";

        return new DocumentMessageDefaults
        {
            DocumentKind = kind,
            DocumentName = documentName,
            Subject = subject,
            Body = body,
            AttachmentDisplayName = SuggestAttachmentFileName(kind, context)
        };
    }

    public static string SuggestAttachmentFileName(DocumentKind kind, CapturedPrintJobContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        string timestamp = (context.CapturedAtUtc ?? DateTimeOffset.Now).ToLocalTime().ToString("yyyyMMdd_HHmm");
        if (kind == DocumentKind.ClinicalDocument)
        {
            return "clinicalDocument_" + timestamp + ".pdf";
        }

        string mrn = SanitizeComponent(context.Mrn);
        if (!string.IsNullOrWhiteSpace(mrn))
        {
            return "MRN" + StripLeadingMrn(mrn) + "_prescription_" + timestamp + ".pdf";
        }

        string patient = SanitizeComponent(context.PatientName);
        if (!string.IsNullOrWhiteSpace(patient))
        {
            return patient + "_prescription_" + timestamp + ".pdf";
        }

        return "prescription_" + timestamp + ".pdf";
    }

    public static string SanitizeComponent(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value.Where(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-').ToArray()).Trim('.', ' ', '_');
    }

    public static string SanitizeAttachmentFileName(string value, string fallback)
    {
        string fallbackName = string.IsNullOrWhiteSpace(fallback) ? "clinicalDocument.pdf" : fallback;
        string fileName = Path.GetFileName((value ?? string.Empty).Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
        string sanitized = new(fileName.Where(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-' or '.').ToArray());
        sanitized = sanitized.Trim('.', ' ', '_');

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = fallbackName;
        }

        string stem = Path.GetFileNameWithoutExtension(sanitized).Trim('.', ' ', '_');
        if (string.IsNullOrWhiteSpace(stem) || ReservedNames.Contains(stem))
        {
            sanitized = fallbackName;
            stem = Path.GetFileNameWithoutExtension(sanitized).Trim('.', ' ', '_');
        }

        sanitized = stem + ".pdf";
        if (sanitized.Length > 120)
        {
            sanitized = sanitized[..116].Trim('.', ' ', '_') + ".pdf";
        }

        return sanitized;
    }

    private static string StripLeadingMrn(string value)
    {
        return value.StartsWith("MRN", StringComparison.OrdinalIgnoreCase) ? value[3..] : value;
    }
}
