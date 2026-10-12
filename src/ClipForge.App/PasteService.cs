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
    public static Task PasteAsync(Clip clip, SelfChangeRegistry? selfChanges = null)
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
        }, StagedContentHash(clip), selfChanges);
    }

    /// <summary>
    /// The content identity of what staging will put on the clipboard, shaped
    /// exactly as a fresh capture of that content would be — which is the
    /// identity the capture path will compute when our own change comes back
    /// around. HTML clips stage as plain text (see PasteAsync), so they hash
    /// as text; RTF stages only the markup, so it hashes as markup.
    /// </summary>
    private static string StagedContentHash(Clip clip) => (clip.Type switch
    {
        ClipType.Text or ClipType.Html => new CapturedClip(ClipType.Text, clip.Text ?? string.Empty,
            null, null, null, null, null, DateTimeOffset.UnixEpoch),
        ClipType.Rtf => new CapturedClip(ClipType.Rtf, null,
            null, null, null, null, null, DateTimeOffset.UnixEpoch, clip.RtfText),
        ClipType.Image => new CapturedClip(ClipType.Image, null,
            clip.ImageData, null, null, null, null, DateTimeOffset.UnixEpoch),
        ClipType.Files => new CapturedClip(ClipType.Files, null,
            null, clip.FilePaths, null, null, null, DateTimeOffset.UnixEpoch),
        // Mandatory discard (unnamed enum values); unreachable from valid producers — fail fast.
        _ => throw new InvalidOperationException($"Invalid ClipType value: {(int)clip.Type}.")
    }).ContentHash;

    /// <summary>
    /// Pastes the plain-text rendering of a clip (formatting stripped). Images and
    /// files have no text form, so they fall back to the rich paste.
    /// </summary>
    public static Task PastePlainTextAsync(Clip clip, SelfChangeRegistry? selfChanges = null)
    {
        ArgumentNullException.ThrowIfNull(clip);
        string? plain = clip.Type switch
        {
            ClipType.Text => clip.Text ?? string.Empty,
            ClipType.Html => StripHtml(clip.Text ?? string.Empty),
            ClipType.Rtf => clip.Text ?? string.Empty, // plain-text companion captured alongside the RTF
            _ => null,
        };
        if (plain is null) return PasteAsync(clip, selfChanges);
        // Staged as plain text, so it hashes as a plain-text capture.
        string stagedHash = new CapturedClip(ClipType.Text, plain,
            null, null, null, null, null, DateTimeOffset.UnixEpoch).ContentHash;
        return PasteCoreAsync(() => WindowsClipboard.TrySetText(plain), stagedHash, selfChanges);
    }

    /// <summary>Best-effort HTML-to-text for paste-as-plain-text (mirrors the capture-side tag strip).</summary>
    private static string StripHtml(string html)
    {
        // MA0009: match timeouts bound hostile input on both passes.
        string noTags = Regex.Replace(html, "<[^>]+>", " ", RegexOptions.None, TimeSpan.FromSeconds(1));
        string collapsed = Regex.Replace(noTags, @"\s+", " ", RegexOptions.None, TimeSpan.FromSeconds(1)).Trim();
        return WebUtility.HtmlDecode(collapsed);
    }

    private static async Task PasteCoreAsync(Func<StageResult> stage, string stagedContentHash, SelfChangeRegistry? selfChanges)
    {
        ArgumentNullException.ThrowIfNull(stage);

        // Snapshot the richest clipboard format so we can put it back afterward,
        // and baseline its sequence number: without a sample taken NOW, a copy
        // landing during the settle delay below is undetectable in principle.
        var previous = ClipboardSnapshot.Capture();
        uint sequenceAtSnapshot = NativeMethods.GetClipboardSequenceNumber();

        RegisterSelfChanges(stagedContentHash, previous, selfChanges);

        await Task.Delay(120).ConfigureAwait(false); // let focus settle back into the target app

        previous = SettleSnapshot(previous, sequenceAtSnapshot, selfChanges);

        var staged = stage();
        if (!staged.Succeeded)
        {
            HandleFailedStage(staged, previous);
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
    /// The failed-stage path. Restores the snapshot only on the transaction's
    /// own evidence: staging provably emptied the clipboard (so the snapshot
    /// is the only copy of what was there) AND the sequence still equals the
    /// one our empty produced (so nothing newer has landed since — a user
    /// copy made during a failed stage is never overwritten). The restore
    /// write re-verifies that sequence inside its own open block, so the
    /// check and the write cannot be raced either. (MA0051: extracted from
    /// PasteCoreAsync.)
    /// </summary>
    private static void HandleFailedStage(StageResult staged, ClipboardSnapshot previous)
    {
        if (ClipboardRestorePolicy.ShouldRestoreAfterFailedStage(
                staged.Outcome, staged.EmptiedSequence, NativeMethods.GetClipboardSequenceNumber()))
            previous.Restore(staged.EmptiedSequence);
    }

    /// <summary>
    /// Identity suppression: register the changes this paste is about to cause
    /// — the staged clip, and the snapshot that will be restored at the end —
    /// so the captures of those changes are recognized as ours and dropped. A
    /// genuine copy of different content, even one made mid-paste, matches
    /// neither registration and is kept. Each hash is registered TWICE,
    /// because each change is seen by the capture path twice: once as the real
    /// clipboard update, and once when the post-paste recovery read re-reads
    /// the final clipboard state (App.OnPasteRequested) — the read that
    /// rescues a genuine copy whose update was missed, and that must not
    /// re-save our own content as a duplicate history entry when it re-reads
    /// it. (MA0051: extracted from PasteCoreAsync.)
    /// </summary>
    private static void RegisterSelfChanges(string stagedContentHash, ClipboardSnapshot previous, SelfChangeRegistry? selfChanges)
    {
        if (selfChanges is null) return;
        selfChanges.Register(stagedContentHash);
        selfChanges.Register(stagedContentHash);
        if (previous.HasContent)
        {
            selfChanges.Register(previous.ContentHash);
            selfChanges.Register(previous.ContentHash);
        }
    }

    /// <summary>
    /// Settles which snapshot the restore may use once the focus-settle delay
    /// has elapsed. If the clipboard moved during the delay, the snapshot is
    /// stale — staging would destroy the newer content and the restore would
    /// put the stale snapshot back over its slot — so it is re-captured, making
    /// the restore target the user's content as it actually is at stage time.
    /// If the clipboard moved AGAIN during that re-capture, no snapshot can be
    /// trusted and the paste restores nothing (ClipboardSnapshot.Empty). A
    /// re-captured snapshot is registered with the self-change registry in
    /// place of the original (whose registration simply expires).
    /// (MA0051: extracted from PasteCoreAsync.)
    /// </summary>
    private static ClipboardSnapshot SettleSnapshot(ClipboardSnapshot snapshot, uint sequenceAtSnapshot, SelfChangeRegistry? selfChanges)
    {
        if (ClipboardRestorePolicy.DecideSnapshotAction(
                sequenceAtSnapshot, NativeMethods.GetClipboardSequenceNumber(), resnapshotAttempted: false)
            != SnapshotAction.Resnapshot)
            return snapshot;

        uint sequenceBeforeRecapture = NativeMethods.GetClipboardSequenceNumber();
        var fresh = ClipboardSnapshot.Capture();
        if (ClipboardRestorePolicy.DecideSnapshotAction(
                sequenceBeforeRecapture, NativeMethods.GetClipboardSequenceNumber(), resnapshotAttempted: true)
            == SnapshotAction.DisableRestore)
            return ClipboardSnapshot.Empty;
        if (fresh.HasContent)
        {
            // Registered twice, like every self-change (see PasteCoreAsync):
            // the restore's own update, and the post-paste recovery read.
            selfChanges?.Register(fresh.ContentHash);
            selfChanges?.Register(fresh.ContentHash);
        }
        return fresh;
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

        /// <summary>True when the snapshot holds a payload a restore could write (and a capture could hash).</summary>
        public bool HasContent => _type switch
        {
            ClipType.Files => _files.Length > 0,
            ClipType.Image => _image is not null,
            ClipType.Rtf => !string.IsNullOrEmpty(_rtf),
            _ => !string.IsNullOrEmpty(_text)
        };

        /// <summary>
        /// The content identity of the snapshot payload, computed with the same
        /// machinery a fresh capture of this content produces — so when the
        /// restore puts this content back on the clipboard and its change comes
        /// around the capture path, the identity matches what PasteCoreAsync
        /// registered before restoring.
        /// </summary>
        public string ContentHash => new CapturedClip(_type, _text, _image, _files,
            null, null, null, DateTimeOffset.UnixEpoch, _rtf).ContentHash;

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
