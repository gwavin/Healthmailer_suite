using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace PrintRxerV3Installer;

[SupportedOSPlatform("windows")]
internal sealed class InstallForm : Form
{
    private readonly Panel _contentPanel = new() { Dock = DockStyle.Fill, Padding = new Padding(20) };
    private readonly TextBox _customHandoffText = new() { Dock = DockStyle.Top };
    private readonly TextBox _reviewText = new() { Dock = DockStyle.Top, ReadOnly = true };
    private readonly TextBox _statusText = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical
    };
    private readonly Panel _statusPanel = new() { Dock = DockStyle.Fill };

    private readonly RadioButton _defaultRadio = new()
    {
        Text = "Use the default local handoff folder",
        Checked = true,
        AutoSize = true
    };

    private readonly RadioButton _customRadio = new()
    {
        Text = "Use a shared or custom handoff folder",
        AutoSize = true
    };

    private readonly Button _browseButton = new() { Text = "Browse..." };
    private readonly Button _backButton = new() { Text = "Back" };
    private readonly Button _nextButton = new() { Text = "Next" };
    private readonly Button _installButton = new() { Text = "Install" };
    private readonly Button _uninstallButton = new() { Text = "Uninstall..." };
    private readonly Button _closeButton = new() { Text = "Close" };

    private string _selectedHandoffRoot = InstallerPaths.DefaultHandoffRoot;

    public InstallForm()
    {
        Text = "printRxer setup";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 620);
        Size = new Size(880, 660);
        Icon = InstallerBranding.TryCreateIcon();

        Controls.Add(_contentPanel);
        ShowFolderStep();
    }

    private void ShowFolderStep()
    {
        _contentPanel.Controls.Clear();

        TableLayoutPanel layout = CreateBaseLayout("Install printRxer", "Choose where printRxer should place HealthMailer handoff packages.");

        Label defaultDescription = CreateWrappedLabel("Recommended for a same-machine test. printRxer will create and use:", 34);

        TextBox defaultPath = new()
        {
            Text = InstallerPaths.DefaultHandoffRoot,
            ReadOnly = true,
            Dock = DockStyle.Fill
        };

        Label customDescription = CreateWrappedLabel("Use this for a shared folder, for example \\\\server\\HealthMailerDrop$\\incoming. You can type or paste a UNC path.", 48);

        _customHandoffText.Text = InstallerPaths.DefaultHandoffRoot;
        _customHandoffText.Enabled = false;
        _browseButton.Enabled = false;
        _defaultRadio.CheckedChanged += (_, _) => UpdateCustomControls();
        _customRadio.CheckedChanged += (_, _) => UpdateCustomControls();
        _browseButton.Click += BrowseClicked;

        TableLayoutPanel customPathRow = new()
        {
            ColumnCount = 2,
            Dock = DockStyle.Top,
            AutoSize = true
        };
        customPathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        customPathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        _browseButton.Dock = DockStyle.Fill;
        customPathRow.Controls.Add(_customHandoffText, 0, 0);
        customPathRow.Controls.Add(_browseButton, 1, 0);

        layout.Controls.Add(_defaultRadio);
        layout.Controls.Add(defaultDescription);
        layout.Controls.Add(defaultPath);
        layout.Controls.Add(Spacer(12));
        layout.Controls.Add(_customRadio);
        layout.Controls.Add(customDescription);
        layout.Controls.Add(customPathRow);
        layout.Controls.Add(Spacer(12));
        layout.Controls.Add(CreateWrappedLabel("Installing the visible printRxer printer requires administrator approval.", 34));
        layout.Controls.Add(CreateButtonRow(_nextButton, _closeButton, _uninstallButton));

        _nextButton.Click -= NextClicked;
        _nextButton.Click += NextClicked;
        _uninstallButton.Click -= UninstallClicked;
        _uninstallButton.Click += UninstallClicked;
        _closeButton.Click -= CloseClicked;
        _closeButton.Click += CloseClicked;

        _contentPanel.Controls.Add(layout);
    }

    private void ShowReviewStep()
    {
        _contentPanel.Controls.Clear();

        TableLayoutPanel layout = CreateBaseLayout("Ready to install", "Review the handoff folder before installing printRxer.");

        _reviewText.Text = _selectedHandoffRoot;
        _reviewText.Height = 26;

        Label reviewLabel = CreateWrappedLabel("printRxer will publish packages to:", 30);

        Label note = CreateWrappedLabel("After installation, Windows should show a printer named printRxer. Setup will not start the watcher unless that printer is present.", 48);

        layout.Controls.Add(reviewLabel);
        layout.Controls.Add(_reviewText);
        layout.Controls.Add(note);
        _statusPanel.Controls.Clear();
        layout.Controls.Add(_statusPanel);
        layout.Controls.Add(CreateButtonRow(_installButton, _closeButton, _backButton));

        _backButton.Click -= BackClicked;
        _backButton.Click += BackClicked;
        _installButton.Click -= InstallClicked;
        _installButton.Click += InstallClicked;
        _closeButton.Click -= CloseClicked;
        _closeButton.Click += CloseClicked;

        _contentPanel.Controls.Add(layout);
    }

    private static TableLayoutPanel CreateBaseLayout(string title, string description)
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            ColumnCount = 1,
            RowCount = 1
        };

        layout.RowStyles.Clear();
        layout.Controls.Add(new Label
        {
            Text = title,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 14, FontStyle.Bold),
            AutoSize = true,
            Dock = DockStyle.Fill
        });
        layout.Controls.Add(CreateWrappedLabel(description, 42));
        layout.Controls.Add(Spacer(10));
        return layout;
    }

    private static Label CreateWrappedLabel(string text, int height)
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

    private static Control Spacer(int height)
    {
        return new Panel { Height = height, Dock = DockStyle.Top };
    }

    private static FlowLayoutPanel CreateButtonRow(params Button[] buttons)
    {
        FlowLayoutPanel row = new()
        {
            AutoSize = true,
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 12, 0, 0)
        };

        foreach (Button button in buttons)
        {
            button.Width = 120;
            button.Height = 44;
            button.Margin = new Padding(8, 0, 0, 0);
            row.Controls.Add(button);
        }

        return row;
    }

    private void UpdateCustomControls()
    {
        bool custom = _customRadio.Checked;
        _customHandoffText.Enabled = custom;
        _browseButton.Enabled = custom;
    }

    private void NextClicked(object? sender, EventArgs e)
    {
        _selectedHandoffRoot = _defaultRadio.Checked ? InstallerPaths.DefaultHandoffRoot : _customHandoffText.Text.Trim();
        if (string.IsNullOrWhiteSpace(_selectedHandoffRoot))
        {
            MessageBox.Show(this, "Enter or browse to the handoff folder.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ShowReviewStep();
    }

    private void BackClicked(object? sender, EventArgs e)
    {
        ShowFolderStep();
    }

    private void UninstallClicked(object? sender, EventArgs e)
    {
        new UninstallForm().ShowDialog(this);
    }

    private void CloseClicked(object? sender, EventArgs e)
    {
        Close();
    }

    private void BrowseClicked(object? sender, EventArgs e)
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = "Select the HealthMailer handoff folder. You may also paste a UNC path directly into the text box.",
            SelectedPath = Directory.Exists(_customHandoffText.Text) ? _customHandoffText.Text : InstallerPaths.DefaultHandoffRoot,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _customHandoffText.Text = dialog.SelectedPath;
        }
    }

    private void InstallClicked(object? sender, EventArgs e)
    {
        DialogResult confirm = MessageBox.Show(
            this,
            "Install printRxer using this handoff folder?\n\n" + _selectedHandoffRoot,
            Text,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        SetBusy(true);
        _statusText.Clear();

        try
        {
            PrintRxerInstaller.Install(new InstallOptions(_selectedHandoffRoot), AppendStatus);
            AppendStatus("Install completed successfully.");
            MessageBox.Show(this, "printRxer was installed successfully. Click OK to close setup.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            AppendStatus("Install failed: " + ex.Message);
            MessageBox.Show(this, ex.Message, "printRxer install failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _backButton.Enabled = !busy;
        _installButton.Enabled = !busy;
        _closeButton.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        Application.DoEvents();
    }

    private void AppendStatus(string message)
    {
        if (_statusText.Parent is null)
        {
            _statusPanel.Controls.Add(_statusText);
        }

        _statusText.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine);
        _statusText.SelectionStart = _statusText.TextLength;
        _statusText.ScrollToCaret();
        Application.DoEvents();
    }
}
