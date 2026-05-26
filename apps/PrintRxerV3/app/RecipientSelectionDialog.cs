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
    private readonly RadioButton _prescriptionKindButton = new();
    private readonly RadioButton _clinicalKindButton = new();
    private readonly TextBox _documentNameBox = new();
    private readonly TextBox _attachmentFilenameBox = new();
    private readonly TextBox _subjectBox = new();
    private readonly TextBox _bodyBox = new();
    private readonly Action? _previewPrescription;
    private readonly Func<RecipientRefreshResult>? _refreshRecipients;
    private readonly Label _recipientSourceLabel = new();
    private readonly System.Windows.Forms.Timer _autoCloseTimer = new();
    private DateTimeOffset? _autoCloseStartedAtUtc;
    private RecipientRecord? _explicitlySelectedRecipient;
    private readonly CapturedPrintJobContext _context;
    private DocumentKind _selectedDocumentKind;
    private bool _suppressGeneratedFieldChangeTracking;
    private bool _subjectWasUserEdited;
    private bool _bodyWasUserEdited;
    private bool _documentNameWasUserEdited;
    private bool _attachmentFilenameWasUserEdited;

    public PickerSelection? Selection { get; private set; }

    public RecipientSelectionDialog(
        IReadOnlyList<RecipientRecord> recipients,
        CapturedPrintJobContext context,
        Action? previewPrescription = null,
        string recipientSourceText = "",
        Func<RecipientRefreshResult>? refreshRecipients = null)
    {
        _context = context;
        _previewPrescription = previewPrescription;
        _refreshRecipients = refreshRecipients;
        _recipients = recipients.OrderBy(recipient => recipient.RecipientName, StringComparer.OrdinalIgnoreCase).ToList();
        Text = "Choose Recipient";
        Width = 1120;
        Height = 920;
        MinimumSize = new System.Drawing.Size(920, 760);
        StartPosition = FormStartPosition.Manual;
        TopMost = true;

        TryApplyIcon();

        TableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 4, ColumnCount = 1 };
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
        PictureBox logo = new() { Width = 80, Height = 80, SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 14, 0) };
        string brandImagePath = GetBrandImagePath();
        if (File.Exists(brandImagePath))
        {
            logo.Image = System.Drawing.Image.FromFile(brandImagePath);
        }

        Label heading = new()
        {
            Text = "Select the recipient and review the document before preparing it for scheduled sending." + Environment.NewLine +
                "1. Search and select the correct pharmacy or clinician." + Environment.NewLine +
                "2. Review the document details, filename, subject, and message." + Environment.NewLine +
                "3. Select Prepare for scheduled sending." + Environment.NewLine +
                "For privacy, this window closes automatically after 03:00.",
            AutoSize = true,
            Font = new System.Drawing.Font(Font.FontFamily, 10, System.Drawing.FontStyle.Regular),
            Dock = DockStyle.Fill,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        };
        header.Controls.Add(logo, 0, 0);
        header.Controls.Add(heading, 1, 0);
        root.Controls.Add(header);

        root.Controls.Add(BuildPrintJobPanel(context));

        ConfigureRecipientGrid();

        SplitContainer mainSplit = new()
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 5,
            FixedPanel = FixedPanel.None,
            Panel1MinSize = 120,
            Panel2MinSize = 120,
            BackColor = System.Drawing.Color.FromArgb(245, 248, 252),
            Margin = new Padding(0, 0, 0, 0),
            TabStop = false
        };
        mainSplit.Panel1.Controls.Add(BuildRecipientColumn(string.IsNullOrWhiteSpace(recipientSourceText) ? "recipient list" : recipientSourceText));
        mainSplit.Panel2.Controls.Add(BuildReviewColumn());
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
        Button previewButton = CreateFooterButton("Preview document", 160);
        Button prepareButton = CreateFooterButton("Prepare for scheduled sending", 220);
        previewButton.Enabled = _previewPrescription is not null;
        prepareButton.Enabled = false;
        buttons.Controls.Add(cancelButton, 1, 0);
        buttons.Controls.Add(previewButton, 2, 0);
        buttons.Controls.Add(prepareButton, 3, 0);
        root.Controls.Add(buttons);

        _searchBox.TextChanged += delegate { RefreshRecipients(); };
        _subjectBox.TextChanged += delegate { if (!_suppressGeneratedFieldChangeTracking) _subjectWasUserEdited = true; };
        _bodyBox.TextChanged += delegate { if (!_suppressGeneratedFieldChangeTracking) _bodyWasUserEdited = true; };
        _documentNameBox.TextChanged += delegate { if (!_suppressGeneratedFieldChangeTracking) _documentNameWasUserEdited = true; };
        _attachmentFilenameBox.TextChanged += delegate { if (!_suppressGeneratedFieldChangeTracking) _attachmentFilenameWasUserEdited = true; };
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
        Load += delegate { StartAutoCloseTimer(); };
        Shown += delegate
        {
            StartAutoCloseTimer();
            PositionOnPrimaryWorkArea();
            BringPickerToFront();
            _searchBox.Focus();
        };
        FormClosing += delegate { _autoCloseTimer.Stop(); };
        Disposed += delegate { _autoCloseTimer.Dispose(); };

        RefreshRecipients();
        InitialiseDocumentDefaults(context);
        UpdateSelectedRecipientField();
        PositionOnPrimaryWorkArea();
        Shown += delegate { SetInitialSplitterDistance(mainSplit); };
    }

    private void SetInitialSplitterDistance(SplitContainer split)
    {
        RecipientPickerSplitterLayout layout = RecipientPickerLayout.CalculateMainSplitter(split.Width, split.SplitterWidth);
        split.Panel1MinSize = layout.Panel1MinSize;
        split.Panel2MinSize = layout.Panel2MinSize;
        split.SplitterDistance = layout.SplitterDistance;
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

    private Control BuildRecipientColumn(string sourceText)
    {
        TableLayoutPanel panel = BuildSectionPanel("Recipient", rowCount: 4);
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        panel.Controls.Add(BuildSectionTitle("Recipient"), 0, 0);
        panel.Controls.Add(BuildRecipientSourcePanel(sourceText), 0, 1);

        _searchBox.PlaceholderText = "Search recipients";
        _searchBox.Dock = DockStyle.Fill;
        _searchBox.Margin = new Padding(0, 4, 0, 8);
        panel.Controls.Add(_searchBox, 0, 2);
        panel.Controls.Add(_recipientGrid, 0, 3);
        return panel;
    }

    private Control BuildReviewColumn()
    {
        TableLayoutPanel panel = BuildSectionPanel("Review and prepare", rowCount: 12);
        for (int index = 0; index < 11; index++)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(BuildSectionTitle("Review and prepare"), 0, 0);
        panel.Controls.Add(BuildSelectedRecipientPanel(), 0, 1);
        panel.Controls.Add(BuildDocumentKindPanel(), 0, 2);

        panel.Controls.Add(FieldLabel("Document name", topMargin: 8), 0, 3);
        _documentNameBox.Dock = DockStyle.Fill;
        panel.Controls.Add(_documentNameBox, 0, 4);

        panel.Controls.Add(BuildAttachmentFilenamePanel(), 0, 5);

        panel.Controls.Add(FieldLabel("Subject", topMargin: 10), 0, 6);
        _subjectBox.Dock = DockStyle.Fill;
        panel.Controls.Add(_subjectBox, 0, 7);

        panel.Controls.Add(FieldLabel("Message", topMargin: 10), 0, 8);
        _bodyBox.Dock = DockStyle.Fill;
        _bodyBox.Multiline = true;
        _bodyBox.ScrollBars = ScrollBars.Vertical;
        _bodyBox.AcceptsReturn = true;
        _bodyBox.AcceptsTab = true;
        _bodyBox.MinimumSize = new System.Drawing.Size(0, 180);
        panel.Controls.Add(_bodyBox, 0, 9);
        panel.SetRowSpan(_bodyBox, 3);
        return panel;
    }

    private TableLayoutPanel BuildSectionPanel(string accessibleName, int rowCount)
    {
        TableLayoutPanel panel = new()
        {
            AccessibleName = accessibleName,
            Dock = DockStyle.Fill,
            RowCount = rowCount,
            ColumnCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(10),
            BackColor = System.Drawing.Color.FromArgb(250, 250, 250)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return panel;
    }

    private Label BuildSectionTitle(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
            Font = new System.Drawing.Font(Font.FontFamily, Font.Size + 1, System.Drawing.FontStyle.Bold),
            ForeColor = System.Drawing.Color.FromArgb(26, 32, 44)
        };
    }

    private Control BuildSelectedRecipientPanel()
    {
        TableLayoutPanel selectedPanel = new() { Dock = DockStyle.Fill, AutoSize = true, RowCount = 3, ColumnCount = 1, Margin = new Padding(0, 0, 0, 6) };
        selectedPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        selectedPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        selectedPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Label selectedLabel = FieldLabel("Selected recipient (read-only)", topMargin: 0);
        _selectedRecipientBox.Dock = DockStyle.Fill;
        _selectedRecipientBox.ReadOnly = true;
        _selectedRecipientBox.TabStop = false;
        _selectedRecipientBox.BackColor = System.Drawing.Color.FromArgb(238, 241, 245);
        _selectedRecipientBox.Text = "No recipient selected yet.";
        Label selectedHelp = new() { Text = "Use the list to change the recipient.", AutoSize = true, Font = new System.Drawing.Font(Font.FontFamily, Font.Size, System.Drawing.FontStyle.Italic), ForeColor = System.Drawing.Color.DimGray, Margin = new Padding(0, 3, 0, 0) };
        selectedPanel.Controls.Add(selectedLabel);
        selectedPanel.Controls.Add(_selectedRecipientBox);
        selectedPanel.Controls.Add(selectedHelp);
        return selectedPanel;
    }

    private Label FieldLabel(string text, int topMargin)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(0, topMargin, 0, 3),
            Font = new System.Drawing.Font(Font.FontFamily, Font.Size, System.Drawing.FontStyle.Bold),
            ForeColor = System.Drawing.Color.FromArgb(45, 55, 72)
        };
    }

    private Control BuildRecipientSourcePanel(string sourceText)
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 4),
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
            Height = 28,
            Margin = new Padding(8, 0, 0, 0),
            UseVisualStyleBackColor = true,
            Enabled = _refreshRecipients is not null
        };
        refreshButton.Click += delegate { RefreshRecipientsFromCentral(refreshButton); };

        panel.Controls.Add(_recipientSourceLabel, 0, 0);
        panel.Controls.Add(refreshButton, 1, 0);
        return panel;
    }

    private Control BuildDocumentKindPanel()
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 4,
            Margin = new Padding(0, 8, 0, 0),
            Padding = Padding.Empty
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        Label label = new() { Text = "Document type:", AutoSize = true, Margin = new Padding(0, 4, 10, 0), Font = new System.Drawing.Font(Font.FontFamily, Font.Size, System.Drawing.FontStyle.Bold) };
        _prescriptionKindButton.Text = "Prescription / Rx";
        _prescriptionKindButton.AutoSize = true;
        _prescriptionKindButton.Margin = new Padding(0, 2, 14, 0);
        _clinicalKindButton.Text = "Clinical document";
        _clinicalKindButton.AutoSize = true;
        _clinicalKindButton.Margin = new Padding(0, 2, 14, 0);

        Button suggestedWordingButton = new()
        {
            Text = "Use suggested wording",
            AutoSize = true,
            Height = 30,
            Margin = new Padding(8, 0, 0, 0),
            UseVisualStyleBackColor = true
        };

        _prescriptionKindButton.CheckedChanged += delegate
        {
            if (_prescriptionKindButton.Checked)
            {
                ApplyDocumentKind(DocumentKind.Prescription, forceAllFields: false);
            }
        };
        _clinicalKindButton.CheckedChanged += delegate
        {
            if (_clinicalKindButton.Checked)
            {
                ApplyDocumentKind(DocumentKind.ClinicalDocument, forceAllFields: false);
            }
        };
        suggestedWordingButton.Click += delegate { ApplyDocumentKind(_selectedDocumentKind, forceAllFields: true); };

        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(_prescriptionKindButton, 1, 0);
        panel.Controls.Add(_clinicalKindButton, 2, 0);
        panel.Controls.Add(suggestedWordingButton, 3, 0);
        return panel;
    }

    private Control BuildAttachmentFilenamePanel()
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 3,
            Margin = new Padding(0, 8, 0, 0),
            Padding = Padding.Empty
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Label label = FieldLabel("Attachment filename", topMargin: 0);
        Button suggestedFilenameButton = new()
        {
            Text = "Use suggested filename",
            AutoSize = true,
            Height = 30,
            Margin = new Padding(8, 0, 0, 0),
            UseVisualStyleBackColor = true
        };
        suggestedFilenameButton.Click += delegate
        {
            SetGeneratedText(_attachmentFilenameBox, DocumentDefaults.Create(_selectedDocumentKind, _context).AttachmentDisplayName);
            _attachmentFilenameWasUserEdited = false;
        };

        _attachmentFilenameBox.Dock = DockStyle.Fill;
        Label note = new()
        {
            Text = "This is the filename the recipient will see. The internal package file remains unchanged for validation.",
            AutoSize = true,
            ForeColor = System.Drawing.Color.DimGray,
            Font = new System.Drawing.Font(Font.FontFamily, Font.Size, System.Drawing.FontStyle.Italic),
            Margin = new Padding(0, 3, 0, 0)
        };

        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(suggestedFilenameButton, 1, 0);
        panel.Controls.Add(_attachmentFilenameBox, 0, 1);
        panel.SetColumnSpan(_attachmentFilenameBox, 2);
        panel.Controls.Add(note, 0, 2);
        panel.SetColumnSpan(note, 2);
        return panel;
    }

    private void InitialiseDocumentDefaults(CapturedPrintJobContext context)
    {
        DocumentKind inferred = DocumentDefaults.InferKind(context);
        _suppressGeneratedFieldChangeTracking = true;
        try
        {
            _prescriptionKindButton.Checked = inferred == DocumentKind.Prescription;
            _clinicalKindButton.Checked = inferred == DocumentKind.ClinicalDocument;
            ApplyDocumentKind(inferred, forceAllFields: true);
        }
        finally
        {
            _suppressGeneratedFieldChangeTracking = false;
        }

        _subjectWasUserEdited = false;
        _bodyWasUserEdited = false;
        _documentNameWasUserEdited = false;
        _attachmentFilenameWasUserEdited = false;
    }

    private void ApplyDocumentKind(DocumentKind kind, bool forceAllFields)
    {
        _selectedDocumentKind = kind;
        DocumentMessageDefaults defaults = DocumentDefaults.Create(kind, _context);
        if (forceAllFields || !_documentNameWasUserEdited)
        {
            SetGeneratedText(_documentNameBox, defaults.DocumentName);
            _documentNameWasUserEdited = false;
        }

        if (forceAllFields || !_subjectWasUserEdited)
        {
            SetGeneratedText(_subjectBox, defaults.Subject);
            _subjectWasUserEdited = false;
        }

        if (forceAllFields || !_bodyWasUserEdited)
        {
            SetGeneratedText(_bodyBox, defaults.Body);
            _bodyWasUserEdited = false;
        }

        if (forceAllFields || !_attachmentFilenameWasUserEdited)
        {
            SetGeneratedText(_attachmentFilenameBox, defaults.AttachmentDisplayName);
            _attachmentFilenameWasUserEdited = false;
        }
    }

    private void SetGeneratedText(TextBox box, string value)
    {
        bool previous = _suppressGeneratedFieldChangeTracking;
        _suppressGeneratedFieldChangeTracking = true;
        try
        {
            box.Text = value;
        }
        finally
        {
            _suppressGeneratedFieldChangeTracking = previous;
        }
    }

    private Control BuildPrintJobPanel(CapturedPrintJobContext context)
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 10,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(10),
            BackColor = System.Drawing.Color.FromArgb(250, 250, 250)
        };
        for (int index = 0; index < 5; index++)
        {
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        }

        PrintJobIdentity identity = PrintJobIdentity.FromContext(context);
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        AddMetadataPair(panel, 0, "Prescribed by", identity.PrescribedBy);
        AddMetadataPair(panel, 2, "Patient", identity.PatientHint);
        AddMetadataPair(panel, 4, "MRN / chart", identity.MrnHint);
        AddMetadataPair(panel, 6, "Printed", identity.CapturedAt);
        AddMetadataPair(panel, 8, "Job / user", identity.JobAndUser);
        return panel;
    }

    private void AddMetadataPair(TableLayoutPanel panel, int column, string label, string value)
    {
        panel.Controls.Add(MetadataLabel(label), column, 0);
        panel.Controls.Add(MetadataValue(value), column + 1, 0);
    }

    private Label MetadataLabel(string text)
    {
        return new Label
        {
            Text = text + ":",
            AutoSize = true,
            Margin = new Padding(0, 2, 6, 2),
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
            MaximumSize = new System.Drawing.Size(180, 0),
            Margin = new Padding(0, 2, 14, 2),
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
            DocumentKind = _selectedDocumentKind,
            DocumentName = string.IsNullOrWhiteSpace(_documentNameBox.Text) ? DocumentDefaults.Create(_selectedDocumentKind, _context).DocumentName : _documentNameBox.Text.Trim(),
            AttachmentDisplayName = DocumentDefaults.SanitizeAttachmentFileName(
                _attachmentFilenameBox.Text,
                DocumentDefaults.Create(_selectedDocumentKind, _context).AttachmentDisplayName),
            SelectedAt = DateTimeOffset.UtcNow
        };
        _autoCloseTimer.Stop();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void AutoCloseIfExpired()
    {
        DateTimeOffset startedAt = _autoCloseStartedAtUtc ?? DateTimeOffset.UtcNow;
        if (!RecipientPickerTimeout.ShouldAutoClose(startedAt, DateTimeOffset.UtcNow, Selection is not null))
        {
            return;
        }

        _autoCloseTimer.Stop();
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void StartAutoCloseTimer()
    {
        _autoCloseStartedAtUtc ??= DateTimeOffset.UtcNow;
        if (!_autoCloseTimer.Enabled)
        {
            _autoCloseTimer.Start();
        }
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

public static class RecipientPickerTimeout
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    public static bool ShouldAutoClose(DateTimeOffset shownAtUtc, DateTimeOffset nowUtc, bool selectionCompleted)
    {
        return !selectionCompleted && nowUtc - shownAtUtc >= Timeout;
    }
}

public sealed record RecipientPickerSplitterLayout(int Panel1MinSize, int Panel2MinSize, int SplitterDistance);

public static class RecipientPickerLayout
{
    public static RecipientPickerSplitterLayout CalculateMainSplitter(int width, int splitterWidth)
    {
        int safeWidth = Math.Max(width, 1);
        int requestedMinimum = Math.Min(360, Math.Max(120, safeWidth / 4));
        int maximumSharedMinimum = Math.Max(0, (safeWidth - splitterWidth - 20) / 2);
        int safeMinimum = Math.Min(requestedMinimum, maximumSharedMinimum);
        int preferredLeft = Math.Max(safeMinimum, (int)Math.Floor(safeWidth * 0.58));
        int maximumLeft = Math.Max(safeMinimum, safeWidth - safeMinimum);
        int splitterDistance = Math.Min(preferredLeft, maximumLeft);
        splitterDistance = Math.Max(safeMinimum, Math.Min(splitterDistance, safeWidth - safeMinimum));
        return new RecipientPickerSplitterLayout(safeMinimum, safeMinimum, splitterDistance);
    }
}
