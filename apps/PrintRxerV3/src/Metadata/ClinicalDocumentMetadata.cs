namespace PrintRxerV3.Metadata;

public sealed record ClinicalDocumentMetadata
{
    public string? PatientName { get; init; }
    public string? Mrn { get; init; }
    public string? PrescribedBy { get; init; }

    public static ClinicalDocumentMetadata FromGlyphText(IReadOnlyList<string> values)
    {
        string? patientName = ValueAfterLabel(values, "Patient Name");
        string? mrn = ValueAfterLabel(values, "Hospital Number/MRN") ??
            ValueAfterLabel(values, "MRN") ??
            ValueAfterLabel(values, "Hospital Number");
        string? prescribedBy = ValueAfterLabel(values, "Prescribed by", skipLikelyDates: true);

        return new ClinicalDocumentMetadata
        {
            PatientName = patientName,
            Mrn = mrn,
            PrescribedBy = prescribedBy
        };
    }

    private static string? ValueAfterLabel(IReadOnlyList<string> values, string label, bool skipLikelyDates = false)
    {
        for (int index = 0; index < values.Count; index++)
        {
            string current = values[index].Trim().TrimEnd(':');
            if (!current.Equals(label, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            for (int valueIndex = index + 1; valueIndex < values.Count; valueIndex++)
            {
                string value = values[valueIndex].Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (IsKnownLabel(value))
                {
                    return null;
                }

                if (skipLikelyDates && IsLikelyDateOrTime(value))
                {
                    continue;
                }

                return value;
            }
        }

        return null;
    }

    private static bool IsKnownLabel(string value)
    {
        string normalized = value.Trim().TrimEnd(':');
        return normalized.Equals("Patient Name", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Hospital Number/MRN", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("MRN", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Hospital Number", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Consultant", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Ward/Department", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Date Written/Issued", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Prescribed by", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Prescription Details", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyDateOrTime(string value)
    {
        return DateTimeOffset.TryParse(value, out _) ||
            System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d{1,2}/\d{1,2}/\d{2,4}(?:\s+\d{1,2}:\d{2})?$");
    }
}
