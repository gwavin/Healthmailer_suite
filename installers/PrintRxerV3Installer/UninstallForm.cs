using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace PrintRxerV3Installer;

[SupportedOSPlatform("windows")]
internal sealed class UninstallForm : Form
{
    private readonly CheckBox _removeData = new()
    {
        Text = "Remove local ProgramData too - lab reset only",
        AutoSize = true
    };

    private readonly TextBox _statusText = new()
    {
        Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical
    };

    private readonly Button _uninstallButton = new() { Text = "Uninstall" };
    private readonly Button _closeButton = new() { Text = "Close" };

    public UninstallForm()
    {
        Text = "printRxer uninstaller";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(700, 400);
        Size = new Size(760, 470);
        Icon = InstallerBranding.TryCreateIcon();

        Label title = new()
        {
            Text = "Uninstall printRxer",
            Font = new Font(Font.FontFamily, 14, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(18, 18)
        };

        Label description = new()
        {
            Text = "This removes the printRxer watcher and the visible printRxer capture printer. Local ProgramData evidence is preserved by default.",
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

        Controls.AddRange([title, description, _removeData, _uninstallButton, _closeButton, _statusText]);
    }

    private void UninstallClicked(object? sender, EventArgs e)
    {
        if (!PrintRxerUninstaller.IsInstalled())
        {
            _statusText.Clear();
            if (!_removeData.Checked || !PrintRxerUninstaller.HasLocalData())
            {
                AppendStatus("printRxer is not installed on this machine.");
                MessageBox.Show(this, "printRxer is not installed on this machine. Nothing needs to be removed.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }

        string message = _removeData.Checked
            ? "This will remove printRxer and delete C:\\ProgramData\\printRxer. Continue?"
            : "This will remove printRxer while preserving C:\\ProgramData\\printRxer. Continue?";

        DialogResult confirm = MessageBox.Show(this, message, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        SetBusy(true);
        _statusText.Clear();

        try
        {
            PrintRxerUninstaller.Uninstall(_removeData.Checked, AppendStatus);
            AppendStatus("Uninstall completed successfully.");
            MessageBox.Show(this, "printRxer uninstall completed. Click OK to close setup.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            string reviewMessage = FriendlyMessage(ex);
            AppendStatus("Uninstall needs review: " + reviewMessage);
            MessageBox.Show(this, reviewMessage, "printRxer uninstall needs review", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _uninstallButton.Enabled = !busy;
        _removeData.Enabled = !busy;
        _closeButton.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        Application.DoEvents();
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
            return "Windows reported a printer cleanup problem. printRxer attempted to remove the application, task, printer, driver, port, and monitor. If any printer component remains visible, restart Windows and run uninstall once more.";
        }

        return message;
    }
}
