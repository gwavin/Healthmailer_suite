using PrintRxerV3.Notifications;

namespace PrintRxerV3.Tests;

public sealed class UserNotificationTests
{
    [Test]
    public void BuildPackageReadyMessage_names_handoff_folder_and_scheduled_sending()
    {
        string packageFolder = @"C:\ProgramData\printRxer\handoff\pkg-1";

        UserNotificationMessage message = UserNotificationMessageBuilder.BuildPackageReadyMessage(packageFolder);

        Assert.Equal("printRxer package ready", message.Title);
        Assert.Contains(packageFolder, message.Body);
        Assert.Contains("clinical document", message.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HealthMailer", message.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scheduled sending", message.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sent", message.Body, StringComparison.OrdinalIgnoreCase);
    }
}
