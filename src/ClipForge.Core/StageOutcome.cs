namespace ClipForge.Core;

/// <summary>
/// How far a clipboard staging attempt got. The writers traverse these states
/// internally; reporting them (instead of collapsing everything into one bool)
/// is what lets the paste failure path attribute a clipboard change correctly:
/// a sequence-number delta alone cannot distinguish "staging emptied the
/// clipboard" from "another app changed it while staging was failing".
/// </summary>
public enum StageOutcome
{
    /// <summary>Payload preparation failed, or the clipboard was never opened. We did not touch the clipboard.</summary>
    NotTouched,

    /// <summary>
    /// The clipboard was opened and <c>EmptyClipboard</c> succeeded — the
    /// clipboard was emptied by us — but handing the payload to the system
    /// then failed (or faulted). The pre-paste snapshot is the only copy of
    /// what was there.
    /// </summary>
    Emptied,

    /// <summary>The payload was handed to the system; staging succeeded.</summary>
    Set
}
