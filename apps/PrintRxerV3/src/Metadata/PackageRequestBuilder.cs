namespace PrintRxerV3.Metadata;

public static class PackageRequestBuilder
{
    public const string DefaultAuditNote = "printRxer created this HealthMailer handoff package as workstation audit evidence; it did not send mail.";

    public static PackageRequest Create(
        string packageId,
        string pdfSha256,
        DateTimeOffset createdAt,
        WorkstationIdentity identity,
        PrintJobOrigin printOrigin,
        PickerSelection selection)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new ArgumentException("Package ID is required.", nameof(packageId));
        }

        if (string.IsNullOrWhiteSpace(pdfSha256))
        {
            throw new ArgumentException("PDF SHA256 is required.", nameof(pdfSha256));
        }

        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(printOrigin);
        ArgumentNullException.ThrowIfNull(selection);
        string documentName = string.IsNullOrWhiteSpace(selection.DocumentName) ? "Clinical document" : selection.DocumentName.Trim();
        string fallbackAttachment = selection.DocumentKind == DocumentKind.Prescription ? "prescription.pdf" : "clinicalDocument.pdf";
        string attachmentDisplayName = DocumentDefaults.SanitizeAttachmentFileName(selection.AttachmentDisplayName, fallbackAttachment);

        return new PackageRequest
        {
            PackageId = packageId,
            CreatedAt = createdAt,
            PreparedAt = createdAt,
            WorkstationIdentity = identity,
            PrintJobOrigin = printOrigin,
            PickerSelection = selection with
            {
                DocumentName = documentName,
                AttachmentDisplayName = attachmentDisplayName
            },
            SelectedRecipient = new SelectedRecipientMetadata
            {
                Name = selection.RecipientName,
                Email = selection.RecipientEmail,
                SelectedAt = selection.SelectedAt
            },
            PickerOutcome = "RecipientSelected",
            SelectedRecipientName = selection.RecipientName,
            SelectedRecipientEmail = selection.RecipientEmail,
            Subject = selection.Subject,
            Body = selection.Body,
            DocumentKind = selection.DocumentKind,
            DocumentName = documentName,
            AttachmentDisplayName = attachmentDisplayName,
            PdfSha256 = pdfSha256,
            AuditNote = DefaultAuditNote
        };
    }
}
