namespace PrintRxerV3.Metadata;

public sealed record PickerSelection
{
    public required string RecipientName { get; init; }
    public required string RecipientEmail { get; init; }
    public required string Subject { get; init; }
    public required string Body { get; init; }
    public DocumentKind DocumentKind { get; init; } = DocumentKind.ClinicalDocument;
    public string DocumentName { get; init; } = "Clinical document";
    public string AttachmentDisplayName { get; init; } = "clinicalDocument.pdf";
    public required DateTimeOffset SelectedAt { get; init; }
}
