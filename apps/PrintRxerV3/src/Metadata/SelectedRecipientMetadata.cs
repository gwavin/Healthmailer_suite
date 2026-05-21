namespace PrintRxerV3.Metadata;

public sealed record SelectedRecipientMetadata
{
    public required string Name { get; init; }
    public required string Email { get; init; }
    public required DateTimeOffset SelectedAt { get; init; }
}
