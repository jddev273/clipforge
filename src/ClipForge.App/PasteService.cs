using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ClipForge.Core;
using ClipForge.Core.Native;
using static ClipForge.Core.Native.NativeMethods;

namespace ClipForge.App;

/// <summary>
/// Pastes a clip into the previously focused app: hides the overlay (returning focus),
/// stages the clip on the clipboard, sends Ctrl+V, then best-effort restores the
/// richest clipboard format the user had before. Restore is skipped when the user
/// copied something new mid-paste (their copy always wins).
/// </summary>
public static class PasteService
{
    public static Task PasteAsync(Clip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        return PasteCoreAsync(() => clip.Type switch
        {
            ClipType.Text => WindowsClipboard.TrySetText(clip.Text ?? string.Empty),
            ClipType.Html => WindowsClipboard.TrySetText(clip.Text ?? string.Empty),
            ClipType.Rtf => string.IsNullOrEmpty(clip.RtfText) ? StageResult.NotTouched : WindowsClipboard.TrySetRtf(clip.RtfText),
            ClipType.Image => clip.ImageData is null ? StageResult.NotTouched : WindowsClipboard.TrySetImage(clip.ImageData),
            ClipType.Files => WindowsClipboard.TrySetFileList(clip.FilePaths),
            // Mandatory discard (unnamed enum values); unreachable from valid producers — fail fast.
            _ => throw new InvalidOperationException($"Invalid ClipType value: {(int)clip.Type}.")
        });
    }

    /// <summary>
    /// Pastes the plain-text rendering of a clip (formatting stripped). Images and
    /// files have no text form, so they fall back to the rich paste.
    /// </summary>
    public static Task PastePlainTextAsync(Clip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        string? plain = clip.Type switch
        {
            ClipType.Text => clip.Text ?? string.Empty,
            ClipType.Html => StripHtml(clip.Text ?? string.Empty),
            ClipType.Rtf => clip.Text ?? string.Empty, // plain-text companion captured alongside the RTF
            _ => null,
        };
        return plain is null ? PasteAsync(clip) : PasteCoreAsync(() => WindowsClipboard.TrySetText(plain));
    }

    /// <summary>Best-effort HTML-to-text for paste-as-plain-text (mirrors the capture-side tag strip).</summary>
    private static string StripHtml(string html)
    {
        // MA0009: match timeouts bound hostile input on both passes.
        string noTags = Regex.Replace(html, "<[^>]+>", " ", RegexOptions.None, TimeSpan.FromSeconds(1));
        string collapsed = Regex.Replace(noTags, @"\s+", " ", RegexOptions.None, TimeSpan.FromSeconds(1)).Trim();
        return WebUtility.HtmlDecode(collapsed);
    }

    private static async Task PasteCoreAsync(Func<StageResult> stage)
    {
        ArgumentNullException.ThrowIfNull(stage);

        // Snapshot the richest clipboard format so we can put it back afterward,
        // and baseline its sequence number: without a sample taken NOW, a copy
        // landing during the settle delay below is undetectable in principle.
        var previous = ClipboardSnapshot.Capture();
        uint sequenceAtSnapshot = NativeMethods.GetClipboardSequenceNumber();

        await Task.Delay(120).ConfigureAwait(false); // let focus settle back into the target app

        // Did the clipboard move while focus settled? Then the snapshot is
        // stale — staging would destroy the newer content and the restore
        // would put the stale snapshot back over its slot. Re-capture instead,
        // so the restore target is the user's content as it actually is now.
        if (ClipboardRestorePolicy.DecideSnapshotAction(
                sequenceAtSnapshot, NativeMethods.GetClipboardSequenceNumber(), resnapshotAttempted: false)
            == SnapshotAction.Resnapshot)
        {
            uint sequenceBeforeRecapture = NativeMethods.GetClipboardSequenceNumber();
            previous = ClipboardSnapshot.Capture();
            // Moved AGAIN during the re-capture: no snapshot can be trusted to
            // match the user's state, so this paste restores nothing.
            if (ClipboardRestorePolicy.DecideSnapshotAction(
                    sequenceBeforeRecapture, NativeMethods.GetClipboardSequenceNumber(), resnapshotAttempted: true)
                == SnapshotAction.DisableRestore)
                previous = ClipboardSnapshot.Empty;
        }

        var staged = stage();
        if (!staged.Succeeded)
        {
            // Staging failed. Restore only on the transaction's own evidence:
            // staging provably emptied the clipboard (so the snapshot is the
            // only copy of what was there) AND the sequence still equals the
            // one our empty produced (so nothing newer has landed since — a
            // user copy made during a failed stage is never overwritten). The
            // restore write re-verifies that sequence inside its own open
            // block, so the check and the write cannot be raced either.
            if (ClipboardRestorePolicy.ShouldRestoreAfterFailedStage(
                    staged.Outcome, staged.EmptiedSequence, NativeMethods.GetClipboardSequenceNumber()))
                previous.Restore(staged.EmptiedSequence);
            return;
        }
        // Sequence number AFTER staging identifies the clipboard state we created.
        uint stagedSequence = NativeMethods.GetClipboardSequenceNumber();

        SendCtrlV();
        await Task.Delay(500).ConfigureAwait(false);

        // If the user copied something new while we were pasting, their copy wins:
        // never restore over it. (The restore write re-verifies the same
        // sequence inside its open block — this check is the cheap early-out.)
        if (!ClipboardRestorePolicy.ShouldRestoreAfterPaste(
                stagedSequence, NativeMethods.GetClipboardSequenceNumber())) return;

        previous.Restore(stagedSequence);
    }

    /// <summary>
    /// Best-effort snapshot of the richest clipboard format, so paste can put back what
    /// was there before. The app models a copy as one <see cref="ClipType"/>; restore
    /// mirrors that rather than rebuilding a multi-format clipboard (no HTML-format
    /// writer exists, so an HTML copy's text companion preserves its content).
    /// </summary>
    private sealed class ClipboardSnapshot
    {
        private readonly ClipType _type;
        private readonly string? _text;
        private readonly string? _rtf;
        private readonly byte[]? _image;
        private readonly string[] _files;

        private ClipboardSnapshot(ClipType type, string? text, string? rtf, byte[]? image, string[] files)
        {
            _type = type;
            _text = text;
            _rtf = rtf;
            _image = image;
            _files = files;
        }

        /// <summary>
        /// The no-restore snapshot: taken when the clipboard moved too fast to
        /// baseline (see the re-capture path in PasteCoreAsync). Its Restore is
        /// a no-op — the same empty shape Capture returns for an empty or
        /// unreadable clipboard, whose restores have always been no-ops.
        /// </summary>
        public static ClipboardSnapshot Empty { get; } =
            new(ClipType.Text, null, null, null, Array.Empty<string>());

        public static ClipboardSnapshot Capture()
        {
            // Richest first — mirrors the format-handler priority order.
            if (WindowsClipboard.TryGetFileList(out string[] files) && files.Length > 0)
                return new ClipboardSnapshot(ClipType.Files, null, null, null, files);
            if (WindowsClipboard.TryGetImage(out byte[]? png, out _, out _) && png is not null)
                return new ClipboardSnapshot(ClipType.Image, null, null, png, Array.Empty<string>());
            if (WindowsClipboard.TryGetRtf(out string? rtf) && !string.IsNullOrEmpty(rtf))
                return new ClipboardSnapshot(ClipType.Rtf, null, rtf, null, Array.Empty<string>());
            if (WindowsClipboard.TryGetText(out string? text) && !string.IsNullOrEmpty(text))
                return new ClipboardSnapshot(ClipType.Text, text, null, null, Array.Empty<string>());
            return new ClipboardSnapshot(ClipType.Text, null, null, null, Array.Empty<string>());
        }

        public void Restore(uint? requireSequence = null)
        {
            // Restore must never throw or clobber. PasteCoreAsync passes the
            // sequence of the clipboard state this restore may replace, and the
            // writers re-verify it inside their clipboard-open block — while
            // holding the clipboard open, where no other writer can slip a
            // change between the check and the write. Every setter below is
            // itself best-effort.
            try
            {
                switch (_type)
                {
                    case ClipType.Files when _files.Length > 0:
                        WindowsClipboard.TrySetFileList(_files, requireSequence);
                        break;
                    case ClipType.Image when _image is not null:
                        WindowsClipboard.TrySetImage(_image, requireSequence);
                        break;
                    case ClipType.Rtf when !string.IsNullOrEmpty(_rtf):
                        WindowsClipboard.TrySetRtf(_rtf, requireSequence);
                        break;
                    case ClipType.Text when !string.IsNullOrEmpty(_text):
                        WindowsClipboard.TrySetText(_text, requireSequence);
                        break;
                }
            }
#pragma warning disable CA1031 // why: restore is best-effort by design — a fault here must degrade to "not restored", never propagate.
            catch (Exception ex)
            {
                Debug.WriteLine($"ClipForge: clipboard restore failed: {ex.Message}");
            }
#pragma warning restore CA1031
        }
    }

    private static void SendCtrlV()
    {
        var inputs = new NativeInput[4];
        int size = Marshal.SizeOf<NativeInput>();

        inputs[0] = KeyDown(VK_CONTROL);
        inputs[1] = KeyDown(VK_V);
        inputs[2] = KeyUp(VK_V);
        inputs[3] = KeyUp(VK_CONTROL);

        // Best-effort injection: a zero return means the keystrokes were blocked (e.g. UAC prompt focused).
        uint sent = SendInput((uint)inputs.Length, inputs, size);
        Debug.Assert(sent == inputs.Length, "Ctrl+V keystroke injection was partially delivered or blocked.");

        static NativeInput KeyDown(ushort vk) => new()
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = 0 } }
        };
        static NativeInput KeyUp(ushort vk) => new()
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP } }
        };
    }
}
