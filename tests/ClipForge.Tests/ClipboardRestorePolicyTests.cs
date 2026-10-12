using ClipForge.Core;
using Xunit;

namespace ClipForge.Tests;

/// <summary>
/// The paste failure path: when staging fails, the pre-paste snapshot is
/// restored only on the staging transaction's own evidence — staging provably
/// emptied the clipboard, and the clipboard still holds exactly the state our
/// empty produced. These tests pin that decision. The previous policy restored
/// on ANY sequence-number delta across the stage attempt; that premise could
/// not distinguish our empty from an external copy landing in the same window
/// (e.g. during the open-retry loop, while another app held the clipboard),
/// and restoring then clobbered the external copy with the stale snapshot.
/// That misattribution is what the NotTouched rows below pin as fixed.
/// </summary>
public sealed class ClipboardRestorePolicyTests
{
    // ---- The failed-stage truth table ----

    [Fact]
    public void ExternalChangeDuringAFailedStageIsNotOursToUndo()
    {
        // Staging never touched the clipboard (e.g. the open retries were
        // exhausted while another app held it), but the sequence moved —
        // another app copied something during the attempt. Restoring the
        // snapshot would clobber that copy. The old any-delta policy said yes.
        Assert.False(ClipboardRestorePolicy.ShouldRestoreAfterFailedStage(
            StageOutcome.NotTouched, emptiedSequence: 0, currentSequence: 42));
    }

    [Fact]
    public void UntouchedClipboardAndNoChangeNeedsNoRestore()
    {
        Assert.False(ClipboardRestorePolicy.ShouldRestoreAfterFailedStage(
            StageOutcome.NotTouched, emptiedSequence: 0, currentSequence: 41));
    }

    [Fact]
    public void UntouchedClipboardWithWraparoundDeltaStillNeedsNoRestore()
    {
        Assert.False(ClipboardRestorePolicy.ShouldRestoreAfterFailedStage(
            StageOutcome.NotTouched, emptiedSequence: 0, currentSequence: uint.MaxValue));
    }

    [Fact]
    public void WeEmptiedAndNobodyElseTouchedItRestores()
    {
        // The intended case: our empty succeeded, the payload hand-off failed,
        // and the sequence still equals the one our empty produced — the
        // clipboard holds the empty state we left, and the snapshot is the
        // only copy of what was there.
        Assert.True(ClipboardRestorePolicy.ShouldRestoreAfterFailedStage(
            StageOutcome.Emptied, emptiedSequence: 42, currentSequence: 42));
    }

    [Fact]
    public void ExternalCopyAfterOurEmptyWinsOverTheRestore()
    {
        // We emptied, the set failed, and then the user copied something new
        // onto the emptied clipboard before the decision ran. The sequence has
        // moved past ours; their copy wins.
        Assert.False(ClipboardRestorePolicy.ShouldRestoreAfterFailedStage(
            StageOutcome.Emptied, emptiedSequence: 42, currentSequence: 43));
    }

    [Fact]
    public void EmptiedWithAnUnrelatedCurrentSequenceDoesNotRestore()
    {
        Assert.False(ClipboardRestorePolicy.ShouldRestoreAfterFailedStage(
            StageOutcome.Emptied, emptiedSequence: 42, currentSequence: 7));
    }

    [Fact]
    public void SuccessfulStageNeverEntersTheFailureRestore()
    {
        // The failure-path decision must not fire for a stage that succeeded,
        // even if the sequences happen to line up.
        Assert.False(ClipboardRestorePolicy.ShouldRestoreAfterFailedStage(
            StageOutcome.Set, emptiedSequence: 42, currentSequence: 42));
    }
}
