namespace PrintRxerV3.Notifications;

public sealed record UserNotificationMessage
{
    public required string Title { get; init; }
    public required string Body { get; init; }
}
