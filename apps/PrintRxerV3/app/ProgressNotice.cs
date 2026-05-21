using System.Runtime.Versioning;
using System.Windows.Forms;

namespace PrintRxerV3.App;

[SupportedOSPlatform("windows")]
public sealed class ProgressNotice : Form
{
    public ProgressNotice(string message)
    {
        Text = "Preparing print job";
        Width = 460;
        Height = 190;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        Label label = new()
        {
            Text = message,
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        };
        Controls.Add(label);
    }
}
