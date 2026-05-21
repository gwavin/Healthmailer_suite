using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using PrintRxerV3.Notifications;

namespace PrintRxerV3.App;

[SupportedOSPlatform("windows")]
public static class WindowsInformationAlert
{
    public static void Show(UserNotificationMessage message)
    {
        using NotifyIcon notifyIcon = new()
        {
            Icon = SystemIcons.Information,
            Text = "printRxer",
            Visible = true,
            BalloonTipTitle = message.Title,
            BalloonTipText = message.Body,
            BalloonTipIcon = ToolTipIcon.Info
        };

        notifyIcon.ShowBalloonTip(5000);
        Thread.Sleep(5500);
    }
}
