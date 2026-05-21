namespace PrintRxerV3.Metadata;

public sealed record WorkstationIdentity
{
    public required string WindowsUser { get; init; }
    public required string DomainUser { get; init; }
    public required string UserSid { get; init; }
    public required int SessionId { get; init; }
    public required string WorkstationName { get; init; }
    public required string WorkstationDomain { get; init; }
}
