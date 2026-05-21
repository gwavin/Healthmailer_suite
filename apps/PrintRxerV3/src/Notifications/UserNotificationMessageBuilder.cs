namespace PrintRxerV3.Notifications;

public static class UserNotificationMessageBuilder
{
    public static UserNotificationMessage BuildPackageReadyMessage(string packageFolder)
    {
        if (string.IsNullOrWhiteSpace(packageFolder))
        {
            throw new ArgumentException("Package folder is required.", nameof(packageFolder));
        }

        return new UserNotificationMessage
        {
            Title = "PrintRxer v3 package ready",
            Body = "Your clinical document has been prepared for HealthMailer scheduled sending and placed in: " + packageFolder
        };
    }

    public static UserNotificationMessage BuildPackageQueuedMessage(string localPackageFolder)
    {
        if (string.IsNullOrWhiteSpace(localPackageFolder))
        {
            throw new ArgumentException("Local package folder is required.", nameof(localPackageFolder));
        }

        return new UserNotificationMessage
        {
            Title = "PrintRxer v3 package queued",
            Body = "Package queued locally; handoff folder unavailable; will retry automatically. Local package: " + localPackageFolder
        };
    }
}
