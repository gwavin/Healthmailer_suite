using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace HealthMailerInstaller;

[SupportedOSPlatform("windows")]
internal sealed class UninstallForm : Form
{
    private readonly CheckBox _removeData = new() { Text = "Remove local ProgramData too - lab reset only", AutoSize = true };
    private readonly TextBox _statusText = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly Button _uninstallButton = new() { Text = "Uninstall" };
    private readonly Button _closeButton = new() { Text = "Close" };

    public UninstallForm()
    {
        Text = "HealthMailer uninstaller";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(700, 400);
        Size = new Size(760, 470);
        Icon = InstallerBranding.TryCreateIcon();

        Label title = new() { Text = "Uninstall HealthMailer", Font = new Font(Font.FontFamily, 14, FontStyle.Bold), AutoSize = true, Location = new Point(18, 18) };
        Label description = new()
        {
            Text = "This removes the HealthMailer watcher and application files. Local sent/failed/quarantine evidence is preserved by default.",
            AutoSize = false,
            Location = new Point(20, 55),
            Size = new Size(700, 46)
        };

        _removeData.Location = new Point(20, 112);
        _uninstallButton.Location = new Point(20, 150);
        _uninstallButton.Width = 120;
        _uninstallButton.Height = 44;
        _uninstallButton.Click += UninstallClicked;

        _closeButton.Location = new Point(150, 150);
        _closeButton.Width = 120;
        _closeButton.Height = 44;
        _closeButton.Click += (_, _) => Close();

        _statusText.Location = new Point(20, 215);
        _statusText.Size = new Size(700, 190);
        _statusText.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom;

        Controls.AddRange([title, description, _removeData, _uninstallButton, _closeButton, _statusText]);
    }

    private void UninstallClicked(object? sender, EventArgs e)
    {
        if (!HealthMailerUninstaller.IsInstalled())
        {
            _statusText.Clear();
            AppendStatus("HealthMailer is not installed on this machine.");
            MessageBox.Show(this, "HealthMailer is not installed on this machine. Nothing needs to be removed.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string message = _removeData.Checked
            ? "This will remove HealthMailer and delete C:\\ProgramData\\HealthMailer. Continue?"
            : "This will remove HealthMailer while preserving C:\\ProgramData\\HealthMailer. Continue?";

        if (MessageBox.Show(this, message, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        _uninstallButton.Enabled = false;
        _closeButton.Enabled = false;
        Cursor = Cursors.WaitCursor;
        _statusText.Clear();

        try
        {
            HealthMailerUninstaller.Uninstall(_removeData.Checked, AppendStatus);
            AppendStatus("Uninstall completed successfully.");
            MessageBox.Show(this, "HealthMailer uninstall completed. Click OK to close setup.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            string reviewMessage = FriendlyMessage(ex);
            AppendStatus("Uninstall needs review: " + reviewMessage);
            MessageBox.Show(this, reviewMessage, "HealthMailer uninstall needs review", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _uninstallButton.Enabled = true;
            _closeButton.Enabled = true;
            Cursor = Cursors.Default;
        }
    }

    private void AppendStatus(string message)
    {
        _statusText.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine);
        _statusText.SelectionStart = _statusText.TextLength;
        _statusText.ScrollToCaret();
        Application.DoEvents();
    }

    private static string FriendlyMessage(Exception ex)
    {
        string message = ex.Message;
        if (message.Contains("powershell.exe", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("exited with code", StringComparison.OrdinalIgnoreCase))
        {
            return "Windows reported a cleanup problem. HealthMailer attempted to remove the application, task, and running process. If anything remains visible, restart Windows and run uninstall once more.";
        }

        return message;
    }
}
