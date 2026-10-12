namespace ClipForge.Core;

/// <summary>
/// The decisions behind paste's failure path: after a failed staging attempt,
/// should the pre-paste clipboard snapshot be put back?
///
/// Attribution is the whole problem. A clipboard sequence-number delta cannot
/// say WHO changed the clipboard — staging that failed before touching it
/// (payload preparation failed, or the clipboard stayed locked through every
/// open retry) leaves a delta indistinguishable from an external copy landing
/// in the same window, and restoring on any delta clobbers that external copy
/// with the stale snapshot. So the decision runs on facts the staging
/// transaction itself recorded (<see cref="StageResult"/>): restore only when
/// staging provably emptied the clipboard AND the sequence still equals the
/// one our empty produced — i.e. the clipboard still holds exactly the state
/// we left behind, and nobody else's content is in the way.
/// </summary>
public static class ClipboardRestorePolicy
{
    public static bool ShouldRestoreAfterFailedStage(StageOutcome outcome, uint emptiedSequence, uint currentSequence)
        => outcome == StageOutcome.Emptied && currentSequence == emptiedSequence;

    /// <summary>
    /// The success-path counterpart: after a successful paste, restore the
    /// snapshot only if the clipboard still holds exactly what we staged. A
    /// changed sequence means the user copied something newer mid-paste —
    /// their copy always wins. (The restore write re-verifies this inside its
    /// own clipboard-open block; this check is the cheap early-out.)
    /// </summary>
    public static bool ShouldRestoreAfterPaste(uint stagedSequence, uint currentSequence)
        => currentSequence == stagedSequence;

    /// <summary>
    /// Snapshot freshness across the focus-settle delay. The sequence sampled
    /// at snapshot time is the baseline: unchanged means the snapshot is the
    /// user's current state. Changed means the snapshot went stale during the
    /// delay — re-capture it (once). Changed again across that re-capture
    /// (<paramref name="resnapshotAttempted"/> on the second call) means no
    /// snapshot can be trusted, so restore is disabled for this paste. Under
    /// no path is a stale snapshot restored.
    /// </summary>
    public static SnapshotAction DecideSnapshotAction(uint sequenceAtSnapshot, uint currentSequence, bool resnapshotAttempted)
    {
        if (currentSequence == sequenceAtSnapshot) return SnapshotAction.UseSnapshot;
        return resnapshotAttempted ? SnapshotAction.DisableRestore : SnapshotAction.Resnapshot;
    }
}
