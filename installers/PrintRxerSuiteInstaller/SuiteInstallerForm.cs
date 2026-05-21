using System.Diagnostics;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace PrintRxerSuiteInstaller;

[SupportedOSPlatform("windows")]
internal sealed class SuiteInstallerForm : Form
{
    private readonly TextBox _statusText = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical
    };

    private readonly Button[] _buttons;

    public SuiteInstallerForm()
    {
        Text = "printRxer suite installer";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(860, 620);
        Size = new Size(940, 700);
        Icon = InstallerBranding.TryCreateIcon();

        Button installPrintRxer = CreateButton("Install printRxer", (_, _) => RunSetup(SuitePaths.PrintRxerSetupPath));
        Button installHealthMailer = CreateButton("Install HealthMailer", (_, _) => RunSetup(SuitePaths.HealthMailerSetupPath));
        Button installCapture = CreateButton("Install printRxer printer capture", (_, _) => RunElevatedScript(SuitePaths.CaptureInstallScriptPath));
        Button validate = CreateButton("Validate installation", (_, _) => RunValidation());
        Button openLogs = CreateButton("Open logs folder", (_, _) => OpenLogsFolder());
        Button supportBundle = CreateButton("Create support bundle", (_, _) => CreateSupportBundle());
        Button uninstallRepair = CreateButton("Uninstall / repair", (_, _) => ShowUninstallRepair());
        Button close = CreateButton("Close", (_, _) => Close());

        _buttons = new[]
        {
            installPrintRxer,
            installHealthMailer,
            installCapture,
            validate,
            openLogs,
            supportBundle,
            uninstallRepair,
            close
        };

        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(20)
        };

        root.Controls.Add(new Label
        {
            Text = "printRxer suite",
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 15, FontStyle.Bold),
            AutoSize = true,
            Dock = DockStyle.Top
        });

        root.Controls.Add(new Label
        {
            Text = "Install and validate the printRxer package creator, HealthMailer courier, and printer capture components.",
            Height = 42,
            Dock = DockStyle.Top
        });

        TableLayoutPanel buttonGrid = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(0, 8, 0, 12)
        };
        buttonGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttonGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        foreach (Button button in _buttons)
        {
            buttonGrid.Controls.Add(button);
        }

        root.Controls.Add(buttonGrid);
        root.Controls.Add(_statusText);

        Controls.Add(root);
        AppendStatus("Ready. Run component installs from the release ZIP root.");
    }

    private static Button CreateButton(string text, EventHandler click)
    {
        Button button = new()
        {
            Text = text,
            Width = 400,
            Height = 48,
            Margin = new Padding(0, 0, 12, 12)
        };
        button.Click += click;
        return button;
    }

    private void RunSetup(string setupPath, string arguments = "")
    {
        if (!File.Exists(setupPath))
        {
            MessageBox.Show(this, "The expected installer was not found:\n\n" + setupPath, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult confirm = MessageBox.Show(
            this,
            "Windows will ask for administrator approval to run this component installer.",
            Text,
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Information);

        if (confirm != DialogResult.OK)
        {
            return;
        }

        RunUserAction(() =>
        {
            AppendStatus("Starting elevated " + Path.GetFileName(setupPath) + ".");
            ProcessRunner.Run(setupPath, arguments, elevate: true);
            AppendStatus(Path.GetFileName(setupPath) + " completed.");
        });
    }

    private void RunElevatedScript(string scriptPath)
    {
        if (!File.Exists(scriptPath))
        {
            MessageBox.Show(this, "The expected support script was not found:\n\n" + scriptPath, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult confirm = MessageBox.Show(
            this,
            "Windows will ask for administrator approval for printer capture installation.",
            Text,
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Information);

        if (confirm != DialogResult.OK)
        {
            return;
        }

        RunUserAction(() =>
        {
            AppendStatus("Starting elevated printer capture install.");
            ProcessRunner.PowerShellFile(scriptPath, string.Empty, elevate: true);
            AppendStatus("Printer capture install completed.");
        });
    }

    private void RunValidation()
    {
        if (!File.Exists(SuitePaths.ValidationScriptPath))
        {
            MessageBox.Show(this, "The validation script was not found:\n\n" + SuitePaths.ValidationScriptPath, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        RunUserAction(() =>
        {
            AppendStatus("Running validation checks.");
            string output = ProcessRunner.PowerShellFile(SuitePaths.ValidationScriptPath, requireSuccess: false);
            AppendStatus(string.IsNullOrWhiteSpace(output) ? "Validation completed with no text output." : output);
        });
    }

    private void OpenLogsFolder()
    {
        string target = Directory.Exists(SuitePaths.PrintRxerLogsRoot)
            ? SuitePaths.PrintRxerLogsRoot
            : Directory.Exists(SuitePaths.HealthMailerLogsRoot)
                ? SuitePaths.HealthMailerLogsRoot
                : Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        Process.Start(new ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true
        });
    }

    private void CreateSupportBundle()
    {
        if (!File.Exists(SuitePaths.SupportBundleScriptPath))
        {
            MessageBox.Show(this, "The support bundle script was not found:\n\n" + SuitePaths.SupportBundleScriptPath, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult confirm = MessageBox.Show(
            this,
            "The support bundle excludes PDF payloads by default. Review logs before sending them outside approved support channels.",
            Text,
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);

        if (confirm != DialogResult.OK)
        {
            return;
        }

        RunUserAction(() =>
        {
            Directory.CreateDirectory(SuitePaths.SupportOutputRoot);
            string output = ProcessRunner.PowerShellFile(
                SuitePaths.SupportBundleScriptPath,
                "-OutputRoot \"" + SuitePaths.SupportOutputRoot + "\"",
                requireSuccess: true);
            AppendStatus(output);
            AppendStatus("Support bundle output folder: " + SuitePaths.SupportOutputRoot);
        });
    }

    private void ShowUninstallRepair()
    {
        using Form dialog = new()
        {
            Text = "Uninstall / repair",
            StartPosition = FormStartPosition.CenterParent,
            MinimumSize = new Size(560, 360),
            Size = new Size(600, 380),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(20)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        Button repairPrintRxer = CreateButton("Repair / reinstall printRxer", (_, _) => { dialog.Close(); RunSetup(SuitePaths.PrintRxerSetupPath); });
        Button repairHealthMailer = CreateButton("Repair / reinstall HealthMailer", (_, _) => { dialog.Close(); RunSetup(SuitePaths.HealthMailerSetupPath); });
        Button uninstallPrintRxer = CreateButton("Uninstall printRxer", (_, _) => { dialog.Close(); RunSetup(SuitePaths.PrintRxerSetupPath, "--uninstall"); });
        Button uninstallHealthMailer = CreateButton("Uninstall HealthMailer", (_, _) => { dialog.Close(); RunSetup(SuitePaths.HealthMailerSetupPath, "--uninstall"); });

        foreach (Button button in new[] { repairPrintRxer, repairHealthMailer, uninstallPrintRxer, uninstallHealthMailer })
        {
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(0, 0, 0, 12);
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
            panel.Controls.Add(button);
        }

        dialog.Controls.Add(panel);
        dialog.ShowDialog(this);
    }

    private void RunUserAction(Action action)
    {
        SetBusy(true);
        try
        {
            action();
        }
        catch (Exception ex)
        {
            AppendStatus("Action failed: " + ex.Message);
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        foreach (Button button in _buttons)
        {
            button.Enabled = !busy;
        }

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
}
