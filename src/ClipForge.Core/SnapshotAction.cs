namespace ClipForge.Core;

/// <summary>
/// What to do about the pre-paste clipboard snapshot once the focus-settle
/// delay has elapsed and the clipboard sequence has been re-read. The snapshot
/// is only a valid restore target if the clipboard still holds what it held
/// when the snapshot was taken.
/// </summary>
public enum SnapshotAction
{
    /// <summary>The clipboard did not move during the delay; the snapshot is current.</summary>
    UseSnapshot,

    /// <summary>
    /// The clipboard moved during the delay: the snapshot is stale. Re-capture
    /// it, so the restore target becomes the user's content as it actually is
    /// at stage time — their copy wins, and the paste proceeds.
    /// </summary>
    Resnapshot,

    /// <summary>
    /// The clipboard moved again during the re-capture itself. No snapshot can
    /// be trusted to match the user's state, so this paste gets no restore;
    /// the paste itself still proceeds (restore is best-effort by design).
    /// </summary>
    DisableRestore
}
