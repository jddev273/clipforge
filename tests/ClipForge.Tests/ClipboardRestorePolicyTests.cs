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

    // ---- Snapshot freshness across the focus-settle delay ----
    //
    // The baseline is sampled at snapshot time. A copy landing during the
    // ~120 ms settle delay used to be invisible: staging destroyed it and the
    // restore put the stale snapshot back over its slot. The decision below is
    // what runs at each boundary — it must never end in restoring a snapshot
    // known to be stale.

    [Fact]
    public void UnchangedSequenceAcrossTheDelayKeepsTheSnapshot()
    {
        Assert.Equal(SnapshotAction.UseSnapshot, ClipboardRestorePolicy.DecideSnapshotAction(
            sequenceAtSnapshot: 41, currentSequence: 41, resnapshotAttempted: false));
    }

    [Fact]
    public void CopyDuringTheDelayMakesTheSnapshotStale()
    {
        Assert.Equal(SnapshotAction.Resnapshot, ClipboardRestorePolicy.DecideSnapshotAction(
            sequenceAtSnapshot: 41, currentSequence: 42, resnapshotAttempted: false));
    }

    [Fact]
    public void WraparoundDuringTheDelayIsStillAChange()
    {
        Assert.Equal(SnapshotAction.Resnapshot, ClipboardRestorePolicy.DecideSnapshotAction(
            sequenceAtSnapshot: uint.MaxValue, currentSequence: 0, resnapshotAttempted: false));
    }

    [Fact]
    public void StableAcrossTheRecaptureUsesTheFreshSnapshot()
    {
        // Second call: the "snapshot" baseline is the sequence sampled just
        // before the re-capture, and the clipboard held still across it.
        Assert.Equal(SnapshotAction.UseSnapshot, ClipboardRestorePolicy.DecideSnapshotAction(
            sequenceAtSnapshot: 42, currentSequence: 42, resnapshotAttempted: true));
    }

    [Fact]
    public void SecondChangeDuringTheRecaptureDisablesTheRestore()
    {
        Assert.Equal(SnapshotAction.DisableRestore, ClipboardRestorePolicy.DecideSnapshotAction(
            sequenceAtSnapshot: 42, currentSequence: 43, resnapshotAttempted: true));
    }

    // ---- The success-path restore decision ----

    [Fact]
    public void ClipboardStillHoldingTheStagedContentRestores()
    {
        Assert.True(ClipboardRestorePolicy.ShouldRestoreAfterPaste(
            stagedSequence: 42, currentSequence: 42));
    }

    [Fact]
    public void CopyDuringThePasteWaitDefeatsTheRestore()
    {
        Assert.False(ClipboardRestorePolicy.ShouldRestoreAfterPaste(
            stagedSequence: 42, currentSequence: 43));
    }
}
