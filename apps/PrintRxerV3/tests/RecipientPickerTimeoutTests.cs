using PrintRxerV3.App;
using Xunit;

namespace PrintRxerV3.Tests;

public sealed class RecipientPickerTimeoutTests
{
    [Fact]
    public void ShouldAutoClose_after_timeout_when_selection_not_completed()
    {
        DateTimeOffset shownAt = DateTimeOffset.UtcNow;

        Assert.True(RecipientPickerTimeout.ShouldAutoClose(shownAt, shownAt.AddMinutes(3).AddSeconds(1), selectionCompleted: false));
    }

    [Fact]
    public void ShouldAutoClose_does_not_close_before_timeout_or_after_selection()
    {
        DateTimeOffset shownAt = DateTimeOffset.UtcNow;

        Assert.False(RecipientPickerTimeout.ShouldAutoClose(shownAt, shownAt.AddMinutes(2), selectionCompleted: false));
        Assert.False(RecipientPickerTimeout.ShouldAutoClose(shownAt, shownAt.AddMinutes(4), selectionCompleted: true));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(400)]
    [InlineData(900)]
    public void Splitter_layout_calculation_stays_within_valid_bounds(int width)
    {
        RecipientPickerSplitterLayout layout = RecipientPickerLayout.CalculateMainSplitter(width, splitterWidth: 5);

        Assert.True(layout.Panel1MinSize >= 0);
        Assert.True(layout.Panel2MinSize >= 0);
        Assert.True(layout.SplitterDistance >= layout.Panel1MinSize);
        Assert.True(layout.SplitterDistance <= Math.Max(width, 1) - layout.Panel2MinSize);
    }

    [Fact]
    public void Recipient_picker_no_longer_exposes_redundant_document_name_field()
    {
        string repoRoot = FindRepoRoot();
        string source = File.ReadAllText(Path.Combine(repoRoot, "apps", "PrintRxerV3", "app", "RecipientSelectionDialog.cs"));

        Assert.DoesNotContain("TextBox _documentNameBox", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Document name", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Use suggested wording", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Use suggested filename", source, StringComparison.Ordinal);
        Assert.Contains("DocumentName = DocumentDefaults.Create(_selectedDocumentKind, _context).DocumentName", source, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PrintRxerSuite.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
