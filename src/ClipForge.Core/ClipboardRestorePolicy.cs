namespace ClipForge.Core;

/// <summary>
/// The decision behind paste's failure path: after a failed staging attempt,
/// should the pre-paste clipboard snapshot be put back?
///
/// The clipboard sequence number is the witness. Staging that failed before
/// touching the clipboard (e.g. the clipboard was locked by another app)
/// leaves the sequence unchanged — the user's content is still there, and it
/// may be newer than the snapshot, so restoring would clobber it. A changed
/// sequence means staging got as far as emptying the clipboard and then
/// failed: the snapshot is the only copy of what was there, so restore it.
/// </summary>
public static class ClipboardRestorePolicy
{
    public static bool ShouldRestoreAfterFailedStage(uint sequenceBeforeStage, uint sequenceAfterStage)
        => sequenceAfterStage != sequenceBeforeStage;
}
