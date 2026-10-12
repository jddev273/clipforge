using System.Runtime.InteropServices;

namespace ClipForge.Core;

/// <summary>
/// The outcome of one staging attempt. <see cref="EmptiedSequence"/> is the
/// clipboard sequence number observed immediately after our own empty, sampled
/// inside the clipboard-open block where no other writer can intervene; it is
/// meaningful only when <see cref="Outcome"/> is <see cref="StageOutcome.Emptied"/>
/// (a failed <c>SetClipboardData</c> does not advance the sequence past it).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct StageResult(StageOutcome Outcome, uint EmptiedSequence)
{
    /// <summary>The attempt that never touched the clipboard.</summary>
    public static StageResult NotTouched => new(StageOutcome.NotTouched, 0);

    /// <summary>True when the payload reached the clipboard.</summary>
    public bool Succeeded => Outcome == StageOutcome.Set;
}
