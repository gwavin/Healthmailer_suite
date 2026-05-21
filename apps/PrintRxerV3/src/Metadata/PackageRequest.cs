namespace PrintRxerV3.Metadata;

public sealed record PackageRequest
{
    public required string PackageId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset PreparedAt { get; init; }
    public DateTimeOffset? ReadyAt { get; init; }
    public required WorkstationIdentity WorkstationIdentity { get; init; }
    public required PrintJobOrigin PrintJobOrigin { get; init; }
    public required PickerSelection PickerSelection { get; init; }
    public required SelectedRecipientMetadata SelectedRecipient { get; init; }
    public required string PickerOutcome { get; init; }
    public required string SelectedRecipientName { get; init; }
    public required string SelectedRecipientEmail { get; init; }
    public required string Subject { get; init; }
    public required string Body { get; init; }
    public required string PdfSha256 { get; init; }
    public required string AuditNote { get; init; }
}
