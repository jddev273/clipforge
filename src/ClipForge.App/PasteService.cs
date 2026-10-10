using System.Runtime.InteropServices;
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
    public static async Task PasteAsync(Clip clip)
    {
        // Snapshot current clipboard text so we can put it back afterward (best effort).
        WindowsClipboard.TryGetText(out string? previousText);

        await Task.Delay(120); // let focus settle back into the target app

        bool staged = clip.Type switch
        {
            ClipType.Text => WindowsClipboard.TrySetText(clip.Text ?? string.Empty),
            ClipType.Html => WindowsClipboard.TrySetText(clip.Text ?? string.Empty),
            ClipType.Image when clip.ImageData is not null => WindowsClipboard.TrySetImage(clip.ImageData),
            ClipType.Files => WindowsClipboard.TrySetFileList(clip.FilePaths),
            _ => false
        };
        if (!staged) return;

        SendCtrlV();
        await Task.Delay(500);

        // Restore what was there before, so the paste doesn't clobber the user's clipboard.
        if (previousText is not null)
            WindowsClipboard.TrySetText(previousText);
    }

    private static void SendCtrlV()
    {
        var inputs = new INPUT[4];
        int size = Marshal.SizeOf<INPUT>();

        inputs[0] = KeyDown(VK_CONTROL);
        inputs[1] = KeyDown(VK_V);
        inputs[2] = KeyUp(VK_V);
        inputs[3] = KeyUp(VK_CONTROL);

        SendInput((uint)inputs.Length, inputs, size);

        static INPUT KeyDown(ushort vk) => new()
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = 0 } }
        };
        static INPUT KeyUp(ushort vk) => new()
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP } }
        };
    }
}
