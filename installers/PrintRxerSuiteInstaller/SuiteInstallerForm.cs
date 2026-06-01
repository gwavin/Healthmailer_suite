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
        AppendStatus("Ready. Use this suite installer as the front door; component setup EXEs are internal under payload\\setup.");
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
        bool elevate = setupKind == SetupKind.PrintRxer;

        string message = setupKind switch
        {
            SetupKind.HealthMailer => "HealthMailer setup will run as the current Windows user. Use the Outlook/Healthmail sender account so the scheduled task and Outlook COM automation use the correct profile.",
            SetupKind.PrintRxer when isUninstall => "printRxer uninstall will remove the watcher, app files, printer queue, driver, port, and monitor in one administrator-approved step. Standard uninstall preserves local data, logs, and archives by default.",
            _ => "printRxer setup includes printer capture. Windows will ask for administrator approval while it installs the app files, port monitor, driver, and local printer queue. After install, run validation to confirm the scheduled task principal."
        };

        DialogResult confirm = MessageBox.Show(this, message, Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Information);

        if (confirm != DialogResult.OK)
        {
            return;
        }

        RunUserAction(() =>
        {
            string actionName = isUninstall ? "uninstall" : "setup";
            AppendStatus("Starting " + Path.GetFileName(setupPath) + " " + actionName + ".");
            AppendStatus(ComponentDisplayName(setupKind) + " " + actionName + " is running. Suite buttons are disabled until it completes.");
            if (isUninstall)
            {
                AppendStatus(ComponentDisplayName(setupKind) + " uninstall is running. Suite buttons are disabled while Windows removes " + ComponentDisplayName(setupKind) + " components.");
                if (elevate)
                {
                    AppendStatus("Approve the Windows administrator prompt if it appears. This window will wait for uninstall to finish.");
                }
            }

            long? logStart = setupKind == SetupKind.PrintRxer && isUninstall
                ? TryGetFileLength(SuitePaths.PrintRxerInstallerLogPath)
                : null;
            using Form busyDialog = ShowBusyDialog(ProgressTitle(setupKind, isUninstall), ProgressMessage(setupKind, isUninstall));

            ProcessResult setupResult = ProcessRunner.RunForResult(setupPath, arguments, elevate: elevate, whileWaiting: PumpBusyUi);
            busyDialog.Close();

            if (setupResult.Cancelled)
            {
                AppendStatus(ComponentDisplayName(setupKind) + " " + actionName + " was cancelled before Windows made changes.");
                MessageBox.Show(this, setupResult.Output, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            AppendStatus(Path.GetFileName(setupPath) + " closed with exit code " + setupResult.ExitCode + ".");
            if (!string.IsNullOrWhiteSpace(setupResult.Output))
            {
                AppendStatus(setupResult.Output);
            }
            else if (setupKind == SetupKind.PrintRxer && isUninstall)
            {
                AppendNewLogLines(SuitePaths.PrintRxerInstallerLogPath, logStart);
            }

            if (setupResult.ExitCode != 0)
            {
                throw new InvalidOperationException(Path.GetFileName(setupPath) + " returned exit code " + setupResult.ExitCode + ".");
            }

            if (setupKind == SetupKind.PrintRxer && !isUninstall)
            {
                ValidatePrintRxerAfterSetup();
            }

            if (setupKind == SetupKind.PrintRxer && isUninstall)
            {
                AppendStatus(arguments.Contains("--remove-data", StringComparison.OrdinalIgnoreCase)
                    ? "printRxer uninstall finished. C:\\ProgramData\\printRxer was requested for removal."
                    : "printRxer uninstall finished. Standard uninstall preserves C:\\ProgramData\\printRxer evidence by default.");
            }

            if (setupKind == SetupKind.HealthMailer && isUninstall)
            {
                AppendStatus("HealthMailer uninstall finished. Standard uninstall preserves C:\\ProgramData\\HealthMailer evidence by default.");
            }
        });
    }

    private static string ComponentDisplayName(SetupKind setupKind)
    {
        return setupKind == SetupKind.HealthMailer ? "HealthMailer" : "printRxer";
    }

    private static string ProgressTitle(SetupKind setupKind, bool uninstall)
    {
        return ComponentDisplayName(setupKind) + (uninstall ? " uninstall is running" : " install is running");
    }

    private static string ProgressMessage(SetupKind setupKind, bool uninstall)
    {
        if (uninstall)
        {
            return setupKind == SetupKind.HealthMailer
            ? "Please wait while Windows removes the HealthMailer scheduled task and app files."
            : "Please wait while Windows removes the printRxer watcher, printer queue, driver, port, monitor, and app files.";
        }

        return setupKind == SetupKind.HealthMailer
            ? "Please wait while Windows installs HealthMailer for the Outlook/Healthmail sender user."
            : "Please wait while Windows installs printRxer, including the watcher, printer queue, driver, port, monitor, and app files.";
    }

    private Form ShowBusyDialog(string title, string message)
    {
        Form dialog = new()
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(520, 180),
            MinimumSize = new Size(520, 180),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ControlBox = false,
            ShowInTaskbar = false
        };

        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(18)
        };
        panel.Controls.Add(new Label
        {
            Text = title,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 11, FontStyle.Bold),
            AutoSize = true,
            Dock = DockStyle.Top
        });
        panel.Controls.Add(new Label
        {
            Text = message,
            AutoSize = false,
            Height = 46,
            Dock = DockStyle.Top
        });
        panel.Controls.Add(new ProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 35,
            Dock = DockStyle.Top,
            Height = 24
        });

        dialog.Controls.Add(panel);
        dialog.Show(this);
        dialog.Refresh();
        Application.DoEvents();
        return dialog;
    }

    private static void PumpBusyUi()
    {
        Application.DoEvents();
    }

    private static long? TryGetFileLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch
        {
            return null;
        }
    }

    private void AppendNewLogLines(string path, long? startPosition)
    {
        if (startPosition is null || !File.Exists(path))
        {
            AppendStatus("No printRxer installer log output was available.");
            return;
        }

        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (startPosition.Value > stream.Length)
            {
                startPosition = 0;
            }

            stream.Seek(startPosition.Value, SeekOrigin.Begin);
            using StreamReader reader = new(stream);
            string text = reader.ReadToEnd().Trim();
            AppendStatus(string.IsNullOrWhiteSpace(text)
                ? "No new printRxer installer log lines were written."
                : "printRxer uninstall log:" + Environment.NewLine + text);
        }
        catch (Exception ex)
        {
            AppendStatus("Could not read printRxer installer log: " + ex.Message);
        }
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
            AppendStatus(ProcessRunner.IsAdministrator()
                ? "Suite installer is already running with administrator rights; starting printRxer setup directly."
                : "Suite installer is not elevated; requesting Windows administrator approval for printRxer setup.");
            string arguments = "--quiet --handoff-root \"" + EscapeArgument(handoffRoot) + "\"";
            using Form busyDialog = ShowBusyDialog(ProgressTitle(SetupKind.PrintRxer, uninstall: false), ProgressMessage(SetupKind.PrintRxer, uninstall: false));
            ProcessResult setupResult = ProcessRunner.RunForResult(SuitePaths.PrintRxerSetupPath, arguments, elevate: true, whileWaiting: PumpBusyUi);
            busyDialog.Close();
            if (setupResult.Cancelled)
            {
                AppendStatus("printRxer install was cancelled before Windows made changes.");
                MessageBox.Show(this, setupResult.Output, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

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
            Text = "Install printRxer printing machine",
            StartPosition = FormStartPosition.CenterParent,
            MinimumSize = new Size(820, 520),
            Size = new Size(860, 560),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(20)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        RadioButton defaultRadio = new()
        {
            Text = "Use the default local handoff folder",
            Checked = true,
            AutoSize = true,
            Dock = DockStyle.Top
        };

        RadioButton customRadio = new()
        {
            Text = "Use a shared or custom handoff folder",
            AutoSize = true,
            Dock = DockStyle.Top
        };

        TextBox defaultPath = new()
        {
            Text = @"C:\ProgramData\printRxer\handoff",
            ReadOnly = true,
            Dock = DockStyle.Fill
        };

        TextBox handoffBox = new()
        {
            Text = @"C:\ProgramData\printRxer\handoff",
            Enabled = false,
            Dock = DockStyle.Fill
        };

        Button browse = CreateDialogButton("Browse...", DialogResult.None);
        browse.Enabled = false;
        browse.Click += (_, _) =>
        {
            using FolderBrowserDialog picker = new()
            {
                Description = "Select the HealthMailer handoff folder. You may also paste a UNC path directly into the text box.",
                SelectedPath = Directory.Exists(handoffBox.Text) ? handoffBox.Text : @"C:\ProgramData\printRxer\handoff",
                ShowNewFolderButton = true
            };

            if (picker.ShowDialog(dialog) == DialogResult.OK)
            {
                handoffBox.Text = picker.SelectedPath;
            }
        };

        defaultRadio.CheckedChanged += (_, _) =>
        {
            bool custom = customRadio.Checked;
            handoffBox.Enabled = custom;
            browse.Enabled = custom;
        };
        customRadio.CheckedChanged += (_, _) =>
        {
            bool custom = customRadio.Checked;
            handoffBox.Enabled = custom;
            browse.Enabled = custom;
        };

        TableLayoutPanel pathRow = new() { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        pathRow.Controls.Add(handoffBox, 0, 0);
        pathRow.Controls.Add(browse, 1, 0);

        Button ok = CreateDialogButton("Install", DialogResult.OK);
        Button cancel = CreateDialogButton("Cancel", DialogResult.Cancel);
        FlowLayoutPanel buttons = new()
        {
            AutoSize = true,
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        panel.Controls.Add(new Label
        {
            Text = "Install printRxer",
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 14, FontStyle.Bold),
            AutoSize = true,
            Dock = DockStyle.Top
        });
        panel.Controls.Add(CreateWrappedDialogLabel("Choose the folder where printRxer will place HealthMailer handoff packages.", 42));
        panel.Controls.Add(new Panel { Height = 10, Dock = DockStyle.Top });
        panel.Controls.Add(defaultRadio);
        panel.Controls.Add(CreateWrappedDialogLabel("Recommended for same-machine testing. printRxer will create handoff packages here:", 34));
        panel.Controls.Add(defaultPath);
        panel.Controls.Add(new Panel { Height = 14, Dock = DockStyle.Top });
        panel.Controls.Add(customRadio);
        panel.Controls.Add(CreateWrappedDialogLabel("Use this for two-machine deployment, for example \\\\server\\HealthMailerDrop$\\incoming. This must match the folder configured in HealthMailer.", 50));
        panel.Controls.Add(pathRow);
        panel.Controls.Add(new Panel { Height = 14, Dock = DockStyle.Top });
        panel.Controls.Add(CreateWrappedDialogLabel("Windows will ask for administrator approval while setup installs the app files, watcher task, native port monitor, driver, and local printer queue named printRxer.", 60));
        panel.Controls.Add(new Panel { Dock = DockStyle.Fill });
        panel.Controls.Add(buttons);
        dialog.Controls.Add(panel);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return string.Empty;
        }

        return defaultRadio.Checked ? @"C:\ProgramData\printRxer\handoff" : handoffBox.Text.Trim();
    }

    private static Button CreateDialogButton(string text, DialogResult dialogResult)
    {
        return new Button
        {
            Text = text,
            DialogResult = dialogResult,
            Width = 120,
            Height = 44,
            Margin = new Padding(8, 0, 0, 0)
        };
    }

    private static Label CreateWrappedDialogLabel(string text, int height)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = height,
            MaximumSize = new Size(800, 0)
        };
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
        bool rxExists = Directory.Exists(SuitePaths.PrintRxerLogsRoot);
        bool hmExists = Directory.Exists(SuitePaths.HealthMailerLogsRoot);

        if (!rxExists && !hmExists)
        {
            string message = "No printRxer or HealthMailer log folder exists yet. Install or validate a component first; logs will appear under C:\\ProgramData\\printRxer\\logs or C:\\ProgramData\\HealthMailer\\logs.";
            AppendStatus(message);
            MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using Form dialog = new()
        {
            Text = "Select logs folder",
            StartPosition = FormStartPosition.CenterParent,
            MinimumSize = new Size(460, 240),
            Size = new Size(460, 260),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(20)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        Button openPrintRxer = CreateButton("Open printRxer logs folder", (_, _) => 
        { 
            dialog.Close(); 
            OpenFolder(SuitePaths.PrintRxerLogsRoot); 
        });
        openPrintRxer.Enabled = rxExists;

        Button openHealthMailer = CreateButton("Open HealthMailer logs folder", (_, _) => 
        { 
            dialog.Close(); 
            OpenFolder(SuitePaths.HealthMailerLogsRoot); 
        });
        openHealthMailer.Enabled = hmExists;

        Button cancel = CreateButton("Cancel", (_, _) => { dialog.Close(); });

        foreach (Button button in new[] { openPrintRxer, openHealthMailer, cancel })
        {
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(0, 0, 0, 10);
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
            panel.Controls.Add(button);
        }

        dialog.Controls.Add(panel);
        dialog.ShowDialog(this);
    }

    private static void OpenFolder(string path)
    {
        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
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
        Button uninstallPrintRxer = CreateButton("Uninstall printRxer", (_, _) => { dialog.Close(); RunPrintRxerUninstall(); });
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

    private void RunPrintRxerUninstall()
    {
        PrintRxerInstallState state = GetPrintRxerInstallState();
        if (!state.IsInstalled)
        {
            AppendStatus("printRxer is not currently installed on this machine.");
            if (!state.HasProgramData)
            {
                AppendStatus("Nothing needs to be removed.");
                MessageBox.Show(this, "printRxer is not installed on this machine. Nothing needs to be removed.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            AppendStatus("Preserved local ProgramData exists at C:\\ProgramData\\printRxer.");
            DialogResult resetChoice = MessageBox.Show(
                this,
                "printRxer is not installed on this machine, so there are no application, task, or printer-capture components to uninstall." +
                Environment.NewLine + Environment.NewLine +
                "Preserved local ProgramData still exists at C:\\ProgramData\\printRxer." +
                Environment.NewLine + Environment.NewLine +
                "Choose Yes only for an approved lab reset where local printRxer ProgramData should be removed.",
                "printRxer is not installed",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button2);

            if (resetChoice != DialogResult.Yes)
            {
                AppendStatus("printRxer ProgramData was preserved.");
                return;
            }

            AppendStatus("printRxer is not installed; approved ProgramData removal selected.");
            RunSetup(SuitePaths.PrintRxerSetupPath, SetupKind.PrintRxer, "--uninstall --remove-data --quiet");
            return;
        }

        DialogResult dataChoice = MessageBox.Show(
            this,
            "Standard printRxer uninstall preserves local evidence in C:\\ProgramData\\printRxer, including logs, configuration, archives, and support/audit material." +
            Environment.NewLine + Environment.NewLine +
            "Choose Yes only for an approved lab reset where local printRxer ProgramData should also be removed." +
            Environment.NewLine + Environment.NewLine +
            "Remove C:\\ProgramData\\printRxer too?",
            "printRxer ProgramData",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (dataChoice == DialogResult.Cancel)
        {
            AppendStatus("printRxer uninstall cancelled before ProgramData choice.");
            return;
        }

        string arguments = dataChoice == DialogResult.Yes
            ? "--uninstall --remove-data --quiet"
            : "--uninstall --quiet";

        AppendStatus(dataChoice == DialogResult.Yes
            ? "printRxer uninstall selected with ProgramData removal."
            : "printRxer uninstall selected with ProgramData preserved.");
        RunSetup(SuitePaths.PrintRxerSetupPath, SetupKind.PrintRxer, arguments);
    }

    private static PrintRxerInstallState GetPrintRxerInstallState()
    {
        bool installed = Directory.Exists(SuitePaths.PrintRxerProgramFilesRoot);
        bool hasProgramData = Directory.Exists(SuitePaths.PrintRxerProgramDataRoot);

        string state = ProcessRunner.PowerShell(@"
if (Get-ScheduledTask -TaskName 'printRxer' -ErrorAction SilentlyContinue) { 'task' }
if (Get-ScheduledTask -TaskName 'PrintRxerV3' -ErrorAction SilentlyContinue) { 'task' }
if (Get-ScheduledTask -TaskName 'PrintRxer Agent' -ErrorAction SilentlyContinue) { 'task' }
if (Get-Printer -Name 'printRxer' -ErrorAction SilentlyContinue) { 'printer' }
if (Get-PrinterPort -Name 'printrx:' -ErrorAction SilentlyContinue) { 'port' }
if (Get-PrinterDriver -Name 'PrintRxer XPS Driver' -ErrorAction SilentlyContinue) { 'driver' }
if (Test-Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Print\Monitors\PrintRxer Port Monitor') { 'monitor' }
", requireSuccess: false);

        installed = installed ||
            state.Contains("task", StringComparison.OrdinalIgnoreCase) ||
            state.Contains("printer", StringComparison.OrdinalIgnoreCase) ||
            state.Contains("port", StringComparison.OrdinalIgnoreCase) ||
            state.Contains("driver", StringComparison.OrdinalIgnoreCase) ||
            state.Contains("monitor", StringComparison.OrdinalIgnoreCase);

        return new PrintRxerInstallState(installed, hasProgramData);
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
            if (busy)
            {
                button.Tag ??= button.Text;
                button.Text = button.Text == "Close" ? "Working..." : button.Text;
            }
            else if (button.Tag is string originalText)
            {
                button.Text = originalText;
                button.Tag = null;
            }
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

    private sealed record PrintRxerInstallState(bool IsInstalled, bool HasProgramData);
}
