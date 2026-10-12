using ClipForge.Core;
using Xunit;

namespace ClipForge.Tests;

/// <summary>
/// The paste failure path: when staging fails, the pre-paste snapshot is
/// restored only if staging actually disturbed the clipboard (sequence
/// changed). These tests pin that decision — an unconditional restore would
/// clobber a newer copy the user made while the paste was settling, and a
/// missing restore would lose the content staging emptied.
/// </summary>
public sealed class ClipboardRestorePolicyTests
{
    [Fact]
    public void UnchangedSequenceMeansStagingNeverTouchedTheClipboard()
    {
        Assert.False(ClipboardRestorePolicy.ShouldRestoreAfterFailedStage(41, 41));
    }

    [Fact]
    public void ChangedSequenceMeansStagingEmptiedTheClipboard()
    {
        Assert.True(ClipboardRestorePolicy.ShouldRestoreAfterFailedStage(41, 42));
    }

    [Fact]
    public void AnyChangeCountsNotJustTheNextSequenceNumber()
    {
        Assert.True(ClipboardRestorePolicy.ShouldRestoreAfterFailedStage(7, 99));
    }

    [Fact]
    public void SequenceWraparoundStillCountsAsAChange()
    {
        Assert.True(ClipboardRestorePolicy.ShouldRestoreAfterFailedStage(uint.MaxValue, 0));
    }
}
