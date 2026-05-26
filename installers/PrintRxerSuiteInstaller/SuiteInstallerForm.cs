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

        Button installPrintRxer = CreateButton("Install printRxer printing machine", (_, _) => RunPrintRxerInstall());
        Button installHealthMailer = CreateButton("Install HealthMailer sending machine", (_, _) => RunSetup(SuitePaths.HealthMailerSetupPath, SetupKind.HealthMailer));
        Button sameMachinePilot = CreateButton("Same-machine pilot: install both", (_, _) => RunSameMachinePilot());
        Button validate = CreateButton("Validate installation", (_, _) => RunValidation());
        Button openLogs = CreateButton("Open logs folder", (_, _) => OpenLogsFolder());
        Button supportBundle = CreateButton("Create support bundle", (_, _) => CreateSupportBundle());
        Button uninstallRepair = CreateButton("Advanced / repair", (_, _) => ShowUninstallRepair());
        Button close = CreateButton("Close", (_, _) => Close());

        _buttons = new[]
        {
            installPrintRxer,
            installHealthMailer,
            sameMachinePilot,
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
            Text = "Choose a role: printRxer printing machine, HealthMailer sending machine, or same-machine pilot. Install HealthMailer as the Outlook/Healthmail sender user. printRxer printer capture may ask for administrator approval.",
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

    private void RunSameMachinePilot()
    {
        DialogResult confirm = MessageBox.Show(
            this,
            "This will start the printRxer printing-machine installer, then the HealthMailer sending-machine installer. Use the same handoff folder in both setup windows.",
            Text,
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Information);

        if (confirm != DialogResult.OK)
        {
            return;
        }

        RunPrintRxerInstall();
        RunSetup(SuitePaths.HealthMailerSetupPath, SetupKind.HealthMailer);
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

    private void RunSetup(string setupPath, SetupKind setupKind, string arguments = "")
    {
        if (!File.Exists(setupPath))
        {
            MessageBox.Show(this, "The expected installer was not found:\n\n" + setupPath, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        bool isUninstall = arguments.Contains("--uninstall", StringComparison.OrdinalIgnoreCase);
        bool elevate = setupKind == SetupKind.PrintRxer && !isUninstall;

        string message = setupKind switch
        {
            SetupKind.HealthMailer => "HealthMailer setup will run as the current Windows user. Use the Outlook/Healthmail sender account so the scheduled task and Outlook COM automation use the correct profile.",
            SetupKind.PrintRxer when isUninstall => "printRxer uninstall will check whether printRxer is installed. Standard uninstall preserves local data, logs, and archives by default.",
            _ => "printRxer setup includes printer capture. Windows will ask for administrator approval while it installs the app files, port monitor, driver, and local printer queue. After install, run validation to confirm the scheduled task principal."
        };

        DialogResult confirm = MessageBox.Show(this, message, Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Information);

        if (confirm != DialogResult.OK)
        {
            return;
        }

        RunUserAction(() =>
        {
            AppendStatus("Starting " + Path.GetFileName(setupPath) + ".");
            ProcessResult setupResult = ProcessRunner.RunForResult(setupPath, arguments, elevate: elevate);
            AppendStatus(Path.GetFileName(setupPath) + " closed with exit code " + setupResult.ExitCode + ".");
            if (!string.IsNullOrWhiteSpace(setupResult.Output))
            {
                AppendStatus(setupResult.Output);
            }

            if (setupResult.ExitCode != 0)
            {
                throw new InvalidOperationException(Path.GetFileName(setupPath) + " returned exit code " + setupResult.ExitCode + ".");
            }

            if (setupKind == SetupKind.PrintRxer && !isUninstall)
            {
                ValidatePrintRxerAfterSetup();
            }
        });
    }

    private void RunPrintRxerInstall()
    {
        if (!File.Exists(SuitePaths.PrintRxerSetupPath))
        {
            MessageBox.Show(this, "The expected installer was not found:\n\n" + SuitePaths.PrintRxerSetupPath, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string handoffRoot = PromptForPrintRxerHandoffRoot();
        if (string.IsNullOrWhiteSpace(handoffRoot))
        {
            return;
        }

        DialogResult confirm = MessageBox.Show(
            this,
            "printRxer will install the app, watcher task, native port monitor, driver, and local printer queue named printRxer." +
            Environment.NewLine + Environment.NewLine +
            "Windows will ask for administrator approval." +
            Environment.NewLine + Environment.NewLine +
            "Handoff folder:" + Environment.NewLine + handoffRoot,
            Text,
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Information);

        if (confirm != DialogResult.OK)
        {
            return;
        }

        RunUserAction(() =>
        {
            AppendStatus("Installing printRxer printing machine with handoff folder: " + handoffRoot);
            string arguments = "--quiet --handoff-root \"" + EscapeArgument(handoffRoot) + "\"";
            ProcessResult setupResult = ProcessRunner.RunForResult(SuitePaths.PrintRxerSetupPath, arguments, elevate: true);
            AppendStatus("printRxerSetup.exe closed with exit code " + setupResult.ExitCode + ".");
            if (!string.IsNullOrWhiteSpace(setupResult.Output))
            {
                AppendStatus(setupResult.Output);
            }

            if (setupResult.ExitCode != 0)
            {
                throw new InvalidOperationException("printRxerSetup.exe returned exit code " + setupResult.ExitCode + ".");
            }

            ValidatePrintRxerAfterSetup();
        });
    }

    private string PromptForPrintRxerHandoffRoot()
    {
        using Form dialog = new()
        {
            Text = "printRxer handoff folder",
            StartPosition = FormStartPosition.CenterParent,
            MinimumSize = new Size(640, 260),
            Size = new Size(700, 280),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(20)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        TextBox handoffBox = new()
        {
            Text = @"C:\ProgramData\printRxer\handoff",
            Dock = DockStyle.Fill
        };

        Button browse = CreateButton("Browse...", (_, _) =>
        {
            using FolderBrowserDialog picker = new()
            {
                Description = "Select the HealthMailer handoff folder. You may paste a UNC path directly into the text box.",
                SelectedPath = Directory.Exists(handoffBox.Text) ? handoffBox.Text : @"C:\ProgramData\printRxer\handoff",
                ShowNewFolderButton = true
            };

            if (picker.ShowDialog(dialog) == DialogResult.OK)
            {
                handoffBox.Text = picker.SelectedPath;
            }
        });
        browse.Width = 110;
        browse.Height = 30;

        TableLayoutPanel pathRow = new() { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.Controls.Add(handoffBox, 0, 0);
        pathRow.Controls.Add(browse, 1, 0);

        Button ok = CreateButton("Install", (_, _) => dialog.DialogResult = DialogResult.OK);
        Button cancel = CreateButton("Cancel", (_, _) => dialog.DialogResult = DialogResult.Cancel);
        FlowLayoutPanel buttons = new()
        {
            AutoSize = true,
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.RightToLeft
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        panel.Controls.Add(new Label { Text = "Choose the folder where printRxer will place HealthMailer handoff packages.", AutoSize = true, Dock = DockStyle.Fill });
        panel.Controls.Add(new Label { Text = "Use the default local folder for same-machine testing, or paste a shared UNC path for two-machine deployment.", AutoSize = false, Height = 42, Dock = DockStyle.Fill });
        panel.Controls.Add(pathRow);
        panel.Controls.Add(new Panel());
        panel.Controls.Add(buttons);
        dialog.Controls.Add(panel);

        return dialog.ShowDialog(this) == DialogResult.OK ? handoffBox.Text.Trim() : string.Empty;
    }

    private void ValidatePrintRxerAfterSetup()
    {
        AppendStatus("Validating printRxer printer capture.");
        ProcessResult validation = ProcessRunner.RunForResult(SuitePaths.PrintRxerSetupPath, "--validate");
        if (!string.IsNullOrWhiteSpace(validation.Output))
        {
            AppendStatus(validation.Output);
        }

        if (validation.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "printRxer setup closed, but validation did not find a complete installation. " +
                "Use Advanced / repair > Repair printRxer printer capture, or review C:\\ProgramData\\printRxer\\logs.");
        }

        AppendStatus("printRxer validation succeeded; Windows should show a printer named printRxer.");
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
        string? target = Directory.Exists(SuitePaths.PrintRxerLogsRoot)
            ? SuitePaths.PrintRxerLogsRoot
            : Directory.Exists(SuitePaths.HealthMailerLogsRoot)
                ? SuitePaths.HealthMailerLogsRoot
                : null;

        if (target is null)
        {
            string message = "No printRxer or HealthMailer log folder exists yet. Install or validate a component first; logs will appear under C:\\ProgramData\\printRxer\\logs or C:\\ProgramData\\HealthMailer\\logs.";
            AppendStatus(message);
            MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

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
            Text = "Advanced / repair",
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
            RowCount = 5,
            Padding = new Padding(20)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        Button repairPrintRxer = CreateButton("Repair / reinstall printRxer printing", (_, _) => { dialog.Close(); RunSetup(SuitePaths.PrintRxerSetupPath, SetupKind.PrintRxer); });
        Button repairCapture = CreateButton("Repair printRxer printer capture", (_, _) => { dialog.Close(); RunElevatedScript(SuitePaths.CaptureInstallScriptPath); });
        Button repairHealthMailer = CreateButton("Repair / reinstall HealthMailer", (_, _) => { dialog.Close(); RunSetup(SuitePaths.HealthMailerSetupPath, SetupKind.HealthMailer); });
        Button uninstallPrintRxer = CreateButton("Uninstall printRxer", (_, _) => { dialog.Close(); RunSetup(SuitePaths.PrintRxerSetupPath, SetupKind.PrintRxer, "--uninstall"); });
        Button uninstallHealthMailer = CreateButton("Uninstall HealthMailer", (_, _) => { dialog.Close(); RunSetup(SuitePaths.HealthMailerSetupPath, SetupKind.HealthMailer, "--uninstall"); });

        foreach (Button button in new[] { repairPrintRxer, repairCapture, repairHealthMailer, uninstallPrintRxer, uninstallHealthMailer })
        {
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(0, 0, 0, 12);
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
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

    private static string EscapeArgument(string value)
    {
        return value.Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private enum SetupKind
    {
        PrintRxer,
        HealthMailer
    }
}
