using PrintRxerV3.Metadata;

namespace PrintRxerV3.Tests;

public sealed class ClinicalDocumentMetadataTests
{
    [Test]
    public void FromGlyphText_extracts_patient_name_and_hospital_number()
    {
        ClinicalDocumentMetadata metadata = ClinicalDocumentMetadata.FromGlyphText([
            "Rotunda Hospital",
            "Patient Name",
            "TRANSFUSION ZZZTEST",
            "Hospital Number/MRN:",
            "H04196948"
        ]);

        Assert.Equal("TRANSFUSION ZZZTEST", metadata.PatientName);
        Assert.Equal("H04196948", metadata.Mrn);
    }

    [Test]
    public void FromGlyphText_extracts_prescribed_by_when_available()
    {
        ClinicalDocumentMetadata metadata = ClinicalDocumentMetadata.FromGlyphText([
            "Prescribed by:",
            "Dr Jane Murphy"
        ]);

        Assert.Equal("Dr Jane Murphy", metadata.PrescribedBy);
    }

    [Test]
    public void FromGlyphText_does_not_treat_printed_date_as_prescriber()
    {
        ClinicalDocumentMetadata metadata = ClinicalDocumentMetadata.FromGlyphText([
            "Prescribed by:",
            "12/05/2026 22:55"
        ]);

        Assert.Null(metadata.PrescribedBy);
    }
}
