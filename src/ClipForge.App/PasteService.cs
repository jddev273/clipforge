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
/// clipboard content the user had before.
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
            ClipType.Rtf => !string.IsNullOrEmpty(clip.RtfText) && WindowsClipboard.TrySetRtf(clip.RtfText),
            ClipType.Image => clip.ImageData is not null && WindowsClipboard.TrySetImage(clip.ImageData),
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

    private static async Task PasteCoreAsync(Func<bool> stage)
    {
        ArgumentNullException.ThrowIfNull(stage);

        // Snapshot current clipboard text so we can put it back afterward (best effort).
        WindowsClipboard.TryGetText(out string? previousText);

        await Task.Delay(120).ConfigureAwait(false); // let focus settle back into the target app

        if (!stage()) return;

        SendCtrlV();
        await Task.Delay(500).ConfigureAwait(false);

        // Restore what was there before, so the paste doesn't clobber the user's clipboard.
        if (previousText is not null)
            WindowsClipboard.TrySetText(previousText);
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
