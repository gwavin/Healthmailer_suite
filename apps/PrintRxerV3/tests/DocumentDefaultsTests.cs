using PrintRxerV3.Capture;
using PrintRxerV3.Metadata;
using Xunit;

namespace PrintRxerV3.Tests;

public sealed class DocumentDefaultsTests
{
    [Theory]
    [InlineData("Rx for discharge")]
    [InlineData("Prescription Jane Doe")]
    [InlineData("Medication list")]
    [InlineData("Pharmacy dispense note")]
    public void InferKind_uses_prescription_for_rx_like_metadata(string documentName)
    {
        CapturedPrintJobContext context = CreateContext(documentName);

        Assert.Equal(DocumentKind.Prescription, DocumentDefaults.InferKind(context));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Clinic letter")]
    [InlineData("Discharge summary")]
    public void InferKind_defaults_to_clinical_document_when_uncertain(string documentName)
    {
        CapturedPrintJobContext context = CreateContext(documentName);

        Assert.Equal(DocumentKind.ClinicalDocument, DocumentDefaults.InferKind(context));
    }

    [Fact]
    public void Create_returns_prescription_wording_and_mrn_filename()
    {
        DateTimeOffset captured = new(2026, 5, 26, 14, 30, 0, TimeSpan.Zero);
        CapturedPrintJobContext context = CreateContext("Prescription", captured, patientName: "Jane Doe", mrn: "123456");

        DocumentMessageDefaults defaults = DocumentDefaults.Create(DocumentKind.Prescription, context);

        Assert.Equal("Prescription", defaults.DocumentName);
        Assert.Equal("Prescription", defaults.Subject);
        Assert.Contains("Please see the attached prescription.", defaults.Body);
        Assert.Equal("MRN123456_prescription_" + captured.ToLocalTime().ToString("yyyyMMdd_HHmm") + ".pdf", defaults.AttachmentDisplayName);
    }

    [Fact]
    public void Create_uses_patient_name_when_prescription_mrn_is_missing()
    {
        DateTimeOffset captured = new(2026, 5, 26, 14, 30, 0, TimeSpan.Zero);
        CapturedPrintJobContext context = CreateContext("Prescription", captured, patientName: "John Smith");

        DocumentMessageDefaults defaults = DocumentDefaults.Create(DocumentKind.Prescription, context);

        Assert.Equal("JohnSmith_prescription_" + captured.ToLocalTime().ToString("yyyyMMdd_HHmm") + ".pdf", defaults.AttachmentDisplayName);
    }

    [Fact]
    public void Create_uses_generic_prescription_filename_without_identifiers()
    {
        DateTimeOffset captured = new(2026, 5, 26, 14, 30, 0, TimeSpan.Zero);

        DocumentMessageDefaults defaults = DocumentDefaults.Create(DocumentKind.Prescription, CreateContext("Prescription", captured));

        Assert.Equal("prescription_" + captured.ToLocalTime().ToString("yyyyMMdd_HHmm") + ".pdf", defaults.AttachmentDisplayName);
    }

    [Fact]
    public void Create_returns_clinical_wording_and_generic_clinical_filename()
    {
        DateTimeOffset captured = new(2026, 5, 26, 14, 30, 0, TimeSpan.Zero);
        CapturedPrintJobContext context = CreateContext("Clinic letter", captured, patientName: "Jane Doe", mrn: "123456");

        DocumentMessageDefaults defaults = DocumentDefaults.Create(DocumentKind.ClinicalDocument, context);

        Assert.Equal("Clinical document", defaults.DocumentName);
        Assert.Equal("Clinical document", defaults.Subject);
        Assert.Contains("Please see the attached clinical document.", defaults.Body);
        Assert.Equal("clinicalDocument_" + captured.ToLocalTime().ToString("yyyyMMdd_HHmm") + ".pdf", defaults.AttachmentDisplayName);
    }

    [Theory]
    [InlineData("John Smith", "JohnSmith")]
    [InlineData("John O'Brien", "JohnOBrien")]
    [InlineData("Anne-Marie Smith", "Anne-MarieSmith")]
    [InlineData("MRN 123/45", "MRN12345")]
    public void SanitizeComponent_removes_unsafe_characters(string value, string expected)
    {
        Assert.Equal(expected, DocumentDefaults.SanitizeComponent(value));
    }

    [Theory]
    [InlineData("../../../bad.pdf", "bad.pdf")]
    [InlineData("report.docx", "report.pdf")]
    [InlineData("", "fallback.pdf")]
    [InlineData("CON.pdf", "fallback.pdf")]
    public void SanitizeAttachmentFileName_prevents_paths_and_forces_pdf(string value, string expected)
    {
        Assert.Equal(expected, DocumentDefaults.SanitizeAttachmentFileName(value, "fallback.pdf"));
    }

    private static CapturedPrintJobContext CreateContext(
        string documentName,
        DateTimeOffset? capturedAtUtc = null,
        string patientName = "",
        string mrn = "")
    {
        return new CapturedPrintJobContext
        {
            CaptureDirectory = "capture",
            PayloadPath = "payload.xps",
            DocumentName = documentName,
            PrinterName = "printRxer",
            PrintJobId = "42",
            CapturedAtUtc = capturedAtUtc,
            PatientName = patientName,
            Mrn = mrn
        };
    }
}
