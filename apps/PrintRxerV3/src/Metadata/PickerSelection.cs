namespace PrintRxerV3.Metadata;

public sealed record PickerSelection
{
    public required string RecipientName { get; init; }
    public required string RecipientEmail { get; init; }
    public required string Subject { get; init; }
    public required string Body { get; init; }
    public required DateTimeOffset SelectedAt { get; init; }
}
