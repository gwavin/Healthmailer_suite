namespace PrintRxerV3.Recipients;

public sealed record RecipientRecord
{
    public required string RecipientName { get; init; }
    public required string EmailAddress { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SearchTerms { get; init; } = Array.Empty<string>();
    public required string SearchText { get; init; }
}
