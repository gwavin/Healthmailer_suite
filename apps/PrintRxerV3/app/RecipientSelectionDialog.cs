using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using PrintRxerV3.Capture;
using PrintRxerV3.Metadata;
using PrintRxerV3.Recipients;

namespace PrintRxerV3.App;

[SupportedOSPlatform("windows")]
public sealed class RecipientSelectionDialog : Form
{
    private const double PreferredInitialScreenFraction = 0.75;
    private const int PickerEdgePadding = 16;
    private readonly List<RecipientRecord> _recipients;
    private readonly DataGridView _recipientGrid = new();
    private readonly TextBox _searchBox = new();
    private readonly TextBox _selectedRecipientBox = new();
    private readonly TextBox _subjectBox = new();
    private readonly TextBox _bodyBox = new();
    private readonly Action? _previewPrescription;
    private readonly Func<RecipientRefreshResult>? _refreshRecipients;
    private readonly Label _recipientSourceLabel = new();
    private readonly System.Windows.Forms.Timer _autoCloseTimer = new();
    private readonly DateTimeOffset _autoCloseAtUtc;
    private RecipientRecord? _explicitlySelectedRecipient;

    public PickerSelection? Selection { get; private set; }

    public RecipientSelectionDialog(
        IReadOnlyList<RecipientRecord> recipients,
        CapturedPrintJobContext context,
        Action? previewPrescription = null,
        string recipientSourceText = "",
        Func<RecipientRefreshResult>? refreshRecipients = null)
    {
        _previewPrescription = previewPrescription;
        _refreshRecipients = refreshRecipients;
        _autoCloseAtUtc = DateTimeOffset.UtcNow.AddMinutes(3);
        _recipients = recipients.OrderBy(recipient => recipient.RecipientName, StringComparer.OrdinalIgnoreCase).ToList();
        Text = "Choose Recipient";
        Width = 1120;
        Height = 920;
        MinimumSize = new System.Drawing.Size(920, 760);
        StartPosition = FormStartPosition.Manual;
        TopMost = true;

        TryApplyIcon();

        TableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 6, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        TableLayoutPanel header = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
            BackColor = System.Drawing.Color.FromArgb(245, 248, 252),
            Padding = new Padding(14)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        PictureBox logo = new() { Width = 128, Height = 128, SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 14, 0) };
        string brandImagePath = GetBrandImagePath();
        if (File.Exists(brandImagePath))
        {
            logo.Image = System.Drawing.Image.FromFile(brandImagePath);
        }

        Label heading = new()
        {
            Text = "Select the pharmacy or clinician who should receive this document." + Environment.NewLine +
                "1. Search and select the correct pharmacy or clinician." + Environment.NewLine +
                "2. Review the recipient, subject, and message below." + Environment.NewLine +
                "3. Select Prepare for scheduled sending to create the HealthMailer handoff package." + Environment.NewLine +
                "For privacy, this window closes automatically after 03:00.",
            AutoSize = true,
            Font = new System.Drawing.Font(Font.FontFamily, 11, System.Drawing.FontStyle.Regular),
            Dock = DockStyle.Fill,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        };
        header.Controls.Add(logo, 0, 0);
        header.Controls.Add(heading, 1, 0);
        root.Controls.Add(header);

        root.Controls.Add(BuildPrintJobPanel(context));

        root.Controls.Add(BuildRecipientSourcePanel(string.IsNullOrWhiteSpace(recipientSourceText) ? "recipient list" : recipientSourceText));

        _searchBox.PlaceholderText = "Search recipients";
        _searchBox.Dock = DockStyle.Fill;
        _searchBox.Margin = new Padding(0, 0, 0, 8);
        root.Controls.Add(_searchBox);

        ConfigureRecipientGrid();

        SplitContainer mainSplit = new()
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 5,
            FixedPanel = FixedPanel.None,
            Panel1MinSize = 25,
            Panel2MinSize = 25,
            BackColor = System.Drawing.Color.FromArgb(245, 248, 252),
            Margin = new Padding(0, 0, 0, 0)
        };
        mainSplit.Panel1.Controls.Add(_recipientGrid);

        TableLayoutPanel selectedPanel = new() { Dock = DockStyle.Fill, AutoSize = true, RowCount = 3, ColumnCount = 1, Margin = new Padding(0, 10, 0, 4) };
        selectedPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        selectedPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        selectedPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Label selectedLabel = new() { Text = "Selected Recipient (read-only):", AutoSize = true, Font = new System.Drawing.Font(Font.FontFamily, Font.Size, System.Drawing.FontStyle.Bold) };
        _selectedRecipientBox.Dock = DockStyle.Fill;
        _selectedRecipientBox.ReadOnly = true;
        _selectedRecipientBox.TabStop = false;
        _selectedRecipientBox.BackColor = System.Drawing.Color.FromArgb(238, 241, 245);
        _selectedRecipientBox.Text = "No recipient selected yet.";
        Label selectedHelp = new() { Text = "Use the list above to change the recipient.", AutoSize = true, Font = new System.Drawing.Font(Font.FontFamily, Font.Size, System.Drawing.FontStyle.Italic), ForeColor = System.Drawing.Color.DimGray };
        selectedPanel.Controls.Add(selectedLabel);
        selectedPanel.Controls.Add(_selectedRecipientBox);
        selectedPanel.Controls.Add(selectedHelp);

        TableLayoutPanel detailPanel = new()
        {
            Dock = DockStyle.Fill,
            RowCount = 5,
            ColumnCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        detailPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detailPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detailPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detailPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detailPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        detailPanel.Controls.Add(selectedPanel);

        Label subjectLabel = new() { Text = "Subject", AutoSize = true, Margin = new Padding(0, 12, 0, 4) };
        detailPanel.Controls.Add(subjectLabel);
        _subjectBox.Dock = DockStyle.Fill;
        _subjectBox.Text = "Electronically transmitted clinical document";
        detailPanel.Controls.Add(_subjectBox);

        Label messageLabel = new() { Text = "Message", AutoSize = true, Margin = new Padding(0, 12, 0, 4) };
        detailPanel.Controls.Add(messageLabel);

        _bodyBox.Dock = DockStyle.Fill;
        _bodyBox.Multiline = true;
        _bodyBox.ScrollBars = ScrollBars.Vertical;
        _bodyBox.Text =
            "Hello," + Environment.NewLine + Environment.NewLine +
            "Please see the attached clinical document." + Environment.NewLine + Environment.NewLine +
            "Document: " + (string.IsNullOrWhiteSpace(context.DocumentName) ? "Clinical document" : context.DocumentName) + Environment.NewLine + Environment.NewLine +
            "Kind regards,";
        detailPanel.Controls.Add(_bodyBox);
        mainSplit.Panel2.Controls.Add(detailPanel);
        root.Controls.Add(mainSplit);

        TableLayoutPanel buttons = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            Margin = new Padding(0, 12, 0, 0),
            Padding = Padding.Empty
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        Button cancelButton = CreateFooterButton("Cancel", 100);
        Button previewButton = CreateFooterButton("Preview prescription", 160);
        Button prepareButton = CreateFooterButton("Prepare for scheduled sending", 220);
        previewButton.Enabled = _previewPrescription is not null;
        prepareButton.Enabled = false;
        buttons.Controls.Add(cancelButton, 1, 0);
        buttons.Controls.Add(previewButton, 2, 0);
        buttons.Controls.Add(prepareButton, 3, 0);
        root.Controls.Add(buttons);

        _searchBox.TextChanged += delegate { RefreshRecipients(); };
        _recipientGrid.SelectionChanged += delegate { RefreshPrepareState(prepareButton); };
        _recipientGrid.CellClick += delegate { AcceptCurrentRecipientSelection(); RefreshPrepareState(prepareButton); };
        _recipientGrid.KeyUp += delegate(object? sender, KeyEventArgs args)
        {
            if (args.KeyCode is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End)
            {
                AcceptCurrentRecipientSelection();
                RefreshPrepareState(prepareButton);
            }
        };
        previewButton.Click += delegate { PreviewPrescription(previewButton); };
        prepareButton.Click += delegate { CompleteSelection(); };
        cancelButton.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
        _autoCloseTimer.Interval = 1000;
        _autoCloseTimer.Tick += delegate { AutoCloseIfExpired(); };
        Shown += delegate
        {
            PositionOnPrimaryWorkArea();
            BringPickerToFront();
            _searchBox.Focus();
            _autoCloseTimer.Start();
        };
        FormClosing += delegate { _autoCloseTimer.Stop(); };
        Disposed += delegate { _autoCloseTimer.Dispose(); };

        RefreshRecipients();
        UpdateSelectedRecipientField();
        PositionOnPrimaryWorkArea();
        Shown += delegate { SetInitialSplitterDistance(mainSplit); };
    }

    private void SetInitialSplitterDistance(SplitContainer split)
    {
        split.Panel1MinSize = Math.Min(180, Math.Max(25, split.Height / 4));
        split.Panel2MinSize = Math.Min(220, Math.Max(25, split.Height / 4));
        int preferredTop = _recipientGrid.ColumnHeadersHeight + (int)(_recipientGrid.RowTemplate.Height * 3.5) + 6;
        preferredTop = Math.Max(split.Panel1MinSize, preferredTop);
        int maximumTop = Math.Max(split.Panel1MinSize, split.Height - split.Panel2MinSize);
        split.SplitterDistance = Math.Min(preferredTop, maximumTop);
    }

    private static Button CreateFooterButton(string text, int width)
    {
        return new Button
        {
            Text = text,
            Width = width,
            Height = 36,
            MinimumSize = new System.Drawing.Size(width, 36),
            MaximumSize = new System.Drawing.Size(width, 36),
            Margin = new Padding(8, 0, 0, 0),
            Padding = new Padding(0),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
            UseVisualStyleBackColor = true
        };
    }

    private void ConfigureRecipientGrid()
    {
        _recipientGrid.Dock = DockStyle.Fill;
        _recipientGrid.AllowUserToAddRows = false;
        _recipientGrid.AllowUserToDeleteRows = false;
        _recipientGrid.AllowUserToResizeRows = false;
        _recipientGrid.MultiSelect = false;
        _recipientGrid.ReadOnly = true;
        _recipientGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _recipientGrid.RowHeadersVisible = false;
        _recipientGrid.AutoGenerateColumns = false;
        _recipientGrid.BackgroundColor = System.Drawing.Color.White;
        _recipientGrid.BorderStyle = BorderStyle.None;
        _recipientGrid.CellBorderStyle = DataGridViewCellBorderStyle.None;
        _recipientGrid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        _recipientGrid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        _recipientGrid.EnableHeadersVisualStyles = false;
        _recipientGrid.ColumnHeadersDefaultCellStyle.BackColor = System.Drawing.SystemColors.Control;
        _recipientGrid.ColumnHeadersDefaultCellStyle.ForeColor = System.Drawing.Color.Black;
        _recipientGrid.ColumnHeadersDefaultCellStyle.SelectionBackColor = System.Drawing.Color.FromArgb(224, 228, 234);
        _recipientGrid.ColumnHeadersDefaultCellStyle.SelectionForeColor = System.Drawing.Color.Black;
        _recipientGrid.GridColor = System.Drawing.Color.White;
        _recipientGrid.DefaultCellStyle.SelectionBackColor = System.Drawing.Color.FromArgb(226, 232, 240);
        _recipientGrid.DefaultCellStyle.SelectionForeColor = System.Drawing.Color.Black;
        _recipientGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Recipient", DataPropertyName = nameof(RecipientGridItem.RecipientName), Width = 240 });
        _recipientGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Email", DataPropertyName = nameof(RecipientGridItem.EmailAddress), Width = 260 });
        _recipientGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Aliases", DataPropertyName = nameof(RecipientGridItem.AliasesText), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
    }

    private Control BuildRecipientSourcePanel(string sourceText)
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 8),
            Padding = Padding.Empty
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _recipientSourceLabel.Text = "Recipients: " + sourceText;
        _recipientSourceLabel.AutoSize = true;
        _recipientSourceLabel.Dock = DockStyle.Fill;
        _recipientSourceLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        _recipientSourceLabel.ForeColor = System.Drawing.Color.DimGray;

        Button refreshButton = new()
        {
            Text = "Refresh recipients",
            AutoSize = true,
            Height = 32,
            Margin = new Padding(8, 0, 0, 0),
            UseVisualStyleBackColor = true,
            Enabled = _refreshRecipients is not null
        };
        refreshButton.Click += delegate { RefreshRecipientsFromCentral(refreshButton); };

        panel.Controls.Add(_recipientSourceLabel, 0, 0);
        panel.Controls.Add(refreshButton, 1, 0);
        return panel;
    }

    private Control BuildPrintJobPanel(CapturedPrintJobContext context)
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 4,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(12),
            BackColor = System.Drawing.Color.FromArgb(250, 250, 250)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        PrintJobIdentity identity = PrintJobIdentity.FromContext(context);
        AddMetadataRow(panel, 0, "Prescribed by", identity.PrescribedBy, "Patient", identity.PatientHint);
        AddMetadataRow(panel, 1, "MRN / chart", identity.MrnHint, "Printed", identity.CapturedAt);
        AddMetadataRow(panel, 2, "Job / user", identity.JobAndUser, string.Empty, string.Empty);
        return panel;
    }

    private void AddMetadataRow(TableLayoutPanel panel, int row, string leftLabel, string leftValue, string rightLabel, string rightValue)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(MetadataLabel(leftLabel), 0, row);
        panel.Controls.Add(MetadataValue(leftValue), 1, row);
        if (!string.IsNullOrWhiteSpace(rightLabel))
        {
            panel.Controls.Add(MetadataLabel(rightLabel), 2, row);
            panel.Controls.Add(MetadataValue(rightValue), 3, row);
        }
    }

    private Label MetadataLabel(string text)
    {
        return new Label
        {
            Text = text + ":",
            AutoSize = true,
            Margin = new Padding(0, 3, 8, 3),
            Font = new System.Drawing.Font(Font.FontFamily, Font.Size, System.Drawing.FontStyle.Bold),
            ForeColor = System.Drawing.Color.FromArgb(45, 55, 72)
        };
    }

    private Label MetadataValue(string text)
    {
        return new Label
        {
            Text = string.IsNullOrWhiteSpace(text) ? "Not available" : text,
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(360, 0),
            Margin = new Padding(0, 3, 18, 3),
            ForeColor = System.Drawing.Color.FromArgb(26, 32, 44)
        };
    }

    private void RefreshRecipients()
    {
        string query = _searchBox.Text.Trim().ToLowerInvariant();
        IEnumerable<RecipientRecord> filtered = _recipients;
        if (!string.IsNullOrWhiteSpace(query))
        {
            string[] terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            filtered = filtered.Where(recipient => terms.All(term => recipient.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        List<RecipientGridItem> items = filtered.Take(250).Select(recipient => new RecipientGridItem(recipient)).ToList();
        _explicitlySelectedRecipient = null;
        _recipientGrid.DataSource = items;
        if (items.Count > 0)
        {
            _recipientGrid.ClearSelection();
            _recipientGrid.CurrentCell = _recipientGrid.Rows[0].Cells[0];
            _recipientGrid.ClearSelection();
        }

        UpdateSelectedRecipientField();
    }

    private void RefreshRecipientsFromCentral(Button refreshButton)
    {
        if (_refreshRecipients is null)
        {
            return;
        }

        bool previousEnabled = refreshButton.Enabled;
        refreshButton.Enabled = false;
        try
        {
            RecipientRefreshResult result = _refreshRecipients();
            if (result.Success && result.Snapshot.HasRecipients)
            {
                _recipients.Clear();
                _recipients.AddRange(result.Snapshot.Recipients.OrderBy(recipient => recipient.RecipientName, StringComparer.OrdinalIgnoreCase));
                _recipientSourceLabel.Text = "Recipients: " + FormatRecipientSource(result.Snapshot);
                RefreshRecipients();
                MessageBox.Show(this, "Recipients refreshed successfully.", "Recipients refreshed", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            MessageBox.Show(
                this,
                "The central recipient list could not be refreshed. The current local recipient list is still being used." + Environment.NewLine + Environment.NewLine + result.Message,
                "Recipient refresh unavailable",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            refreshButton.Enabled = previousEnabled;
        }
    }

    private static string FormatRecipientSource(RecipientSnapshot snapshot)
    {
        return snapshot.SourceUsed switch
        {
            RecipientSourceKind.Central => "central list",
            RecipientSourceKind.Cache => AppendWarning("cached central list from " + File.GetLastWriteTime(snapshot.SourcePath).ToString("dd MMM yyyy HH:mm"), snapshot.Warning),
            RecipientSourceKind.BundledFallback => AppendWarning("bundled fallback list", snapshot.Warning),
            _ => "no usable recipient list"
        };
    }

    private static string AppendWarning(string source, string warning)
    {
        return string.IsNullOrWhiteSpace(warning) ? source : source + " - " + warning;
    }

    private void UpdateSelectedRecipientField()
    {
        if (_explicitlySelectedRecipient is RecipientRecord recipient)
        {
            _selectedRecipientBox.Text = recipient.RecipientName + " <" + recipient.EmailAddress + ">";
        }
        else
        {
            _selectedRecipientBox.Text = "No recipient selected yet.";
        }
    }

    private void AcceptCurrentRecipientSelection()
    {
        _explicitlySelectedRecipient = GetCurrentGridRecipient();
        UpdateSelectedRecipientField();
    }

    private void RefreshPrepareState(Button prepareButton)
    {
        prepareButton.Enabled = _explicitlySelectedRecipient is not null;
    }

    private void CompleteSelection()
    {
        RecipientRecord? recipient = _explicitlySelectedRecipient;
        if (recipient is null)
        {
            return;
        }

        Selection = new PickerSelection
        {
            RecipientName = recipient.RecipientName,
            RecipientEmail = recipient.EmailAddress,
            Subject = _subjectBox.Text,
            Body = _bodyBox.Text,
            SelectedAt = DateTimeOffset.UtcNow
        };
        _autoCloseTimer.Stop();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void AutoCloseIfExpired()
    {
        if (Selection is not null || DateTimeOffset.UtcNow < _autoCloseAtUtc)
        {
            return;
        }

        _autoCloseTimer.Stop();
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void PreviewPrescription(Button previewButton)
    {
        if (_previewPrescription is null)
        {
            return;
        }

        bool previousEnabled = previewButton.Enabled;
        previewButton.Enabled = false;
        try
        {
            _previewPrescription();
        }
        catch
        {
            MessageBox.Show(
                this,
                "The preview could not be opened. The document has not been sent or prepared. Please try again, or contact support if this continues.",
                "Preview unavailable",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            previewButton.Enabled = previousEnabled;
            BringPickerToFront();
        }
    }

    private RecipientRecord? GetCurrentGridRecipient()
    {
        if (_recipientGrid.CurrentRow?.DataBoundItem is RecipientGridItem current)
        {
            return current.Recipient;
        }

        if (_recipientGrid.SelectedRows.Count > 0 && _recipientGrid.SelectedRows[0].DataBoundItem is RecipientGridItem selected)
        {
            return selected.Recipient;
        }

        return null;
    }

    private sealed class RecipientGridItem
    {
        public RecipientGridItem(RecipientRecord recipient)
        {
            Recipient = recipient;
        }

        public RecipientRecord Recipient { get; }
        public string RecipientName => Recipient.RecipientName;
        public string EmailAddress => Recipient.EmailAddress;
        public string AliasesText => string.Join("; ", (Recipient.SearchTerms.Count > 0 ? Recipient.SearchTerms : Recipient.Aliases).Take(8));
    }

    private sealed record PrintJobIdentity(
        string PrescribedBy,
        string PatientHint,
        string MrnHint,
        string CapturedAt,
        string JobAndUser)
    {
        public static PrintJobIdentity FromContext(CapturedPrintJobContext context)
        {
            string documentName = string.IsNullOrWhiteSpace(context.DocumentName) ? "Clinical document" : context.DocumentName.Trim();
            string prescribedBy = string.IsNullOrWhiteSpace(context.PrescribedBy) ? string.Empty : context.PrescribedBy.Trim();
            string patientHint = string.IsNullOrWhiteSpace(context.PatientName) ? ExtractPatientHint(documentName) : context.PatientName.Trim();
            string mrnHint = string.IsNullOrWhiteSpace(context.Mrn) ? ExtractMrnHint(documentName) : context.Mrn.Trim();
            string capturedAt = context.CapturedAtUtc is DateTimeOffset captured
                ? captured.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
                : string.Empty;
            string job = string.IsNullOrWhiteSpace(context.PrintJobId) ? "Job not available" : "Job " + context.PrintJobId.Trim();
            string user = string.IsNullOrWhiteSpace(context.SubmittingUser) ? string.Empty : context.SubmittingUser.Trim();
            string jobAndUser = string.IsNullOrWhiteSpace(user) ? job : job + " / " + user;
            return new PrintJobIdentity(prescribedBy, patientHint, mrnHint, capturedAt, jobAndUser);
        }

        private static string ExtractPatientHint(string value)
        {
            Match match = Regex.Match(value ?? string.Empty, @"\b(?:Patient|Name)\s*[:#-]?\s*([A-Za-z][A-Za-z '\-]{1,80})", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
        }

        private static string ExtractMrnHint(string value)
        {
            Match match = Regex.Match(value ?? string.Empty, @"\b(?:MRN|MR|Chart\s*No\.?|FIN|Encounter)\s*[:#-]?\s*([A-Za-z0-9-]{3,32})\b", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
        }
    }

    private void TryApplyIcon()
    {
        string path = GetBrandImagePath();
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using System.Drawing.Bitmap bitmap = new(path);
            Icon = System.Drawing.Icon.FromHandle(bitmap.GetHicon());
        }
        catch
        {
        }
    }

    private void PositionOnPrimaryWorkArea()
    {
        try
        {
            System.Drawing.Rectangle workArea = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromControl(this).WorkingArea;
            int minimumWidth = Math.Min(MinimumSize.Width, workArea.Width);
            int preferredZoneWidth = Math.Max(0, (int)Math.Floor(workArea.Width * PreferredInitialScreenFraction) - PickerEdgePadding);
            int maximumInitialWidth = preferredZoneWidth >= minimumWidth
                ? preferredZoneWidth
                : Math.Max(minimumWidth, workArea.Width - PickerEdgePadding);

            int targetWidth = Math.Min(Width, maximumInitialWidth);
            int targetHeight = Math.Min(Height, workArea.Height);
            targetWidth = Math.Max(targetWidth, minimumWidth);
            targetHeight = Math.Max(targetHeight, Math.Min(MinimumSize.Height, workArea.Height));

            Width = targetWidth;
            Height = targetHeight;
            MaximumSize = new System.Drawing.Size(workArea.Width, workArea.Height);
            int maxLeft = workArea.Right - targetWidth;
            int preferredLeft = workArea.Left + Math.Max(0, Math.Min(PickerEdgePadding, Math.Max(0, maxLeft - workArea.Left)));
            Left = Math.Min(preferredLeft, maxLeft);
            Top = workArea.Top + Math.Max(0, (workArea.Height - targetHeight) / 2);
        }
        catch
        {
        }
    }

    private void BringPickerToFront()
    {
        try
        {
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            Show();
            TopMost = true;
            ShowWindow(Handle, 9);
            BringWindowToTop(Handle);
            SetForegroundWindow(Handle);
            Activate();
            Focus();
            TopMost = false;
            TopMost = true;
        }
        catch
        {
            Activate();
        }
    }

    private static string GetBrandImagePath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "printRxer", "data", "Images", "mncms_400x400.jpg");
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
