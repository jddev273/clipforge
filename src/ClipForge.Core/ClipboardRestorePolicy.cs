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
}
