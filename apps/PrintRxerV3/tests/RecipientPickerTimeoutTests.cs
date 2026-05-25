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
}
