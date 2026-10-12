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

        // Snapshot the richest clipboard format so we can put it back afterward.
        var previous = ClipboardSnapshot.Capture();

        await Task.Delay(120).ConfigureAwait(false); // let focus settle back into the target app

        var staged = stage();
        if (!staged.Succeeded)
        {
            // Staging failed. Restore only on the transaction's own evidence:
            // staging provably emptied the clipboard (so the snapshot is the
            // only copy of what was there) AND the sequence still equals the
            // one our empty produced (so nothing newer has landed since — a
            // user copy made during a failed stage is never overwritten).
            if (ClipboardRestorePolicy.ShouldRestoreAfterFailedStage(
                    staged.Outcome, staged.EmptiedSequence, NativeMethods.GetClipboardSequenceNumber()))
                previous.Restore();
            return;
        }
        // Sequence number AFTER staging identifies the clipboard state we created.
        uint stagedSequence = NativeMethods.GetClipboardSequenceNumber();

        SendCtrlV();
        await Task.Delay(500).ConfigureAwait(false);

        // If the user copied something new while we were pasting, their copy wins:
        // never restore over it.
        if (NativeMethods.GetClipboardSequenceNumber() != stagedSequence) return;

        previous.Restore();
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

        public void Restore()
        {
            // Restore runs after a successful paste and must never throw or clobber:
            // the sequence-number guard in PasteCoreAsync already verified the clipboard
            // still holds what we staged. Every setter below is itself best-effort.
            try
            {
                switch (_type)
                {
                    case ClipType.Files when _files.Length > 0:
                        WindowsClipboard.TrySetFileList(_files);
                        break;
                    case ClipType.Image when _image is not null:
                        WindowsClipboard.TrySetImage(_image);
                        break;
                    case ClipType.Rtf when !string.IsNullOrEmpty(_rtf):
                        WindowsClipboard.TrySetRtf(_rtf);
                        break;
                    case ClipType.Text when !string.IsNullOrEmpty(_text):
                        WindowsClipboard.TrySetText(_text);
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
