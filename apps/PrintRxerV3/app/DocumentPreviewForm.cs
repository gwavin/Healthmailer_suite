using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace PrintRxerV3.App;

[SupportedOSPlatform("windows")]
internal sealed class DocumentPreviewForm : Form
{
    private readonly IReadOnlyList<Image> _pages;
    private readonly PictureBox _pageImage = new();
    private readonly Label _pageLabel = new() { AutoSize = true, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Button _previousButton = CreateButton("Previous page", 120);
    private readonly Button _nextButton = CreateButton("Next page", 120);
    private int _pageIndex;

    public DocumentPreviewForm(IReadOnlyList<byte[]> pageImages)
    {
        if (pageImages.Count == 0)
        {
            throw new ArgumentException("Document preview requires at least one page.", nameof(pageImages));
        }

        _pages = pageImages.Select(LoadImage).ToArray();
        Text = "Document preview";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(980, 860);
        MinimumSize = new Size(640, 520);
        ShowInTaskbar = false;

        _pageImage.Dock = DockStyle.Fill;
        _pageImage.BackColor = Color.FromArgb(64, 68, 74);
        _pageImage.SizeMode = PictureBoxSizeMode.Zoom;

        Button fitButton = CreateButton("Fit", 80);
        Button zoomButton = CreateButton("Actual size", 100);
        Button closeButton = CreateButton("Close", 100);
        closeButton.DialogResult = DialogResult.OK;

        _previousButton.Click += (_, _) => ShowPage(_pageIndex - 1);
        _nextButton.Click += (_, _) => ShowPage(_pageIndex + 1);
        fitButton.Click += (_, _) => _pageImage.SizeMode = PictureBoxSizeMode.Zoom;
        zoomButton.Click += (_, _) => _pageImage.SizeMode = PictureBoxSizeMode.CenterImage;

        FlowLayoutPanel controls = new()
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(8),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        controls.Controls.Add(_previousButton);
        controls.Controls.Add(_nextButton);
        controls.Controls.Add(_pageLabel);
        controls.Controls.Add(fitButton);
        controls.Controls.Add(zoomButton);
        controls.Controls.Add(closeButton);

        Controls.Add(_pageImage);
        Controls.Add(controls);
        AcceptButton = closeButton;
        CancelButton = closeButton;
        FormClosed += (_, _) => DisposePages();
        ShowPage(0);
    }

    private void ShowPage(int index)
    {
        _pageIndex = Math.Clamp(index, 0, _pages.Count - 1);
        _pageImage.Image = _pages[_pageIndex];
        _pageLabel.Text = "Page " + (_pageIndex + 1) + " of " + _pages.Count;
        _previousButton.Enabled = _pageIndex > 0;
        _nextButton.Enabled = _pageIndex < _pages.Count - 1;
    }

    private void DisposePages()
    {
        _pageImage.Image = null;
        foreach (Image page in _pages)
        {
            page.Dispose();
        }
    }

    private static Image LoadImage(byte[] bytes)
    {
        using MemoryStream stream = new(bytes);
        using Image source = Image.FromStream(stream);
        return new Bitmap(source);
    }

    private static Button CreateButton(string text, int width)
    {
        return new Button { Text = text, Width = width, Height = 34, Margin = new Padding(4) };
    }
}
