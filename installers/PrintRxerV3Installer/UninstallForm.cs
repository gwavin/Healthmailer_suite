using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Windows.Forms;

namespace PrintRxerV3Installer;

[SupportedOSPlatform("windows")]
internal sealed class UninstallForm : Form
{
    private readonly CheckBox _removeData = new()
    {
        Text = "Also remove local ProgramData - approved lab reset only",
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
            Text = "This removes printRxer application, watcher, and printer-capture components. Local ProgramData evidence, logs, configuration, and archives are preserved by default.",
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
        Shown += (_, _) => ShowInitialState();
    }

    private void UninstallClicked(object? sender, EventArgs e)
    {
        if (!PrintRxerUninstaller.IsInstalled())
        {
            _statusText.Clear();
            bool hasLocalData = PrintRxerUninstaller.HasLocalData();
            AppendStatus("printRxer is not currently installed on this machine.");

            if (!_removeData.Checked)
            {
                if (hasLocalData)
                {
                    AppendStatus("Preserved local ProgramData exists at C:\\ProgramData\\printRxer.");
                    AppendStatus("Standard uninstall leaves local data, logs, and archives in place.");
                }
                else
                {
                    AppendStatus("Nothing needs to be removed.");
                }

                MessageBox.Show(this, "printRxer is not installed on this machine. Nothing needs to be removed by standard uninstall.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!hasLocalData)
            {
                MessageBox.Show(this, "printRxer is not installed on this machine. Nothing needs to be removed.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            AppendStatus("Lab reset is selected, so preserved local ProgramData can still be removed after confirmation.");
        }

        string message = _removeData.Checked
            ? "This will uninstall printRxer and remove C:\\ProgramData\\printRxer, including local data, logs, configuration, outbox, processed captures, failed captures, and archives. Continue?"
            : "This will uninstall printRxer and preserve C:\\ProgramData\\printRxer, including local data, logs, configuration, outbox, processed captures, failed captures, and archives. Continue?";

        DialogResult confirm = MessageBox.Show(this, message, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        if (PrintRxerUninstaller.IsInstalled() && !IsAdministrator())
        {
            AppendStatus("printRxer printer capture is installed and requires administrator approval to remove.");
            if (TryRelaunchElevated())
            {
                Close();
            }

            return;
        }

        SetBusy(true);
        _statusText.Clear();
        AppendStatus("Uninstall is running. Buttons are disabled until this step completes.");

        try
        {
            PrintRxerUninstaller.Uninstall(_removeData.Checked, AppendStatus);
            if (PrintRxerUninstaller.IsInstalled())
            {
                string reviewMessage = "printRxer uninstall needs review. Windows still reports one or more installed printRxer components. Restart Windows and run uninstall again as an administrator.";
                AppendStatus(reviewMessage);
                MessageBox.Show(this, reviewMessage, "printRxer uninstall needs review", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_removeData.Checked && PrintRxerUninstaller.HasLocalData())
            {
                string reviewMessage = "printRxer application components were removed, but local ProgramData could not be fully removed. It may be in use and can be removed after restart.";
                AppendStatus(reviewMessage);
                MessageBox.Show(this, reviewMessage, "printRxer uninstall needs review", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

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

    private bool TryRelaunchElevated()
    {
        DialogResult approval = MessageBox.Show(
            this,
            "Removing printRxer printer capture requires administrator approval. Windows will ask for permission, then complete the already-confirmed uninstall.",
            Text,
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Information);

        if (approval != DialogResult.OK)
        {
            AppendStatus("Administrator relaunch was cancelled before UAC.");
            return false;
        }

        try
        {
            string exePath = Environment.ProcessPath ?? Application.ExecutablePath;
            string arguments = _removeData.Checked ? "--uninstall --remove-data --quiet" : "--uninstall --quiet";
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas"
            });
            AppendStatus("Elevated printRxer uninstaller was started and will complete the already-confirmed uninstall.");
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            AppendStatus("Administrator approval was cancelled.");
            return false;
        }
        catch (Exception ex)
        {
            AppendStatus("Could not start elevated uninstaller: " + ex.Message);
            MessageBox.Show(this, "Could not start the elevated printRxer uninstaller:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private static bool IsAdministrator()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private void ShowInitialState()
    {
        if (PrintRxerUninstaller.IsInstalled())
        {
            AppendStatus("Ready to uninstall printRxer. Local ProgramData evidence is preserved by default.");
            if (!IsAdministrator())
            {
                AppendStatus("Administrator approval will be required to remove printer-capture components.");
            }

            return;
        }

        AppendStatus("printRxer is not currently installed on this machine.");
        if (PrintRxerUninstaller.HasLocalData())
        {
            AppendStatus("Preserved local ProgramData exists at C:\\ProgramData\\printRxer.");
            AppendStatus("Use the lab reset option only if removal of local data, logs, and archives has been approved.");
        }
        else
        {
            AppendStatus("Nothing needs to be removed.");
        }
    }

    private void SetBusy(bool busy)
    {
        _uninstallButton.Enabled = !busy;
        _removeData.Enabled = !busy;
        _closeButton.Enabled = !busy;
        _uninstallButton.Text = busy ? "Uninstalling..." : "Uninstall";
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
