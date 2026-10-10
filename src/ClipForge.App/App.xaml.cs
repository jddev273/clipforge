using System.Diagnostics;
using System.IO;
using System.Windows;
using ClipForge.Core;
using ClipForge.Core.Native;

namespace ClipForge.App;

/// <summary>
/// App bootstrap: SQLite store → clipboard listener → global hotkey → overlay → tray.
/// No main window at launch; the overlay is summoned with Ctrl+Shift+V.
/// </summary>
public partial class App : System.Windows.Application
{
    private const int OverlayCacheSize = 500;

    private ClipboardListener? _listener;
    private HotkeyService? _hotkeys;
    private TrayIcon? _tray;
    private OverlayWindow? _overlay;
    private IClipStore? _store;
    private string _lastSignature = string.Empty;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClipForge", "clips.db");

        _store = new SqliteClipStore(dbPath);
        await _store.InitializeAsync();
        // Privacy-by-default: retention is NOT indefinite.
        await _store.PruneByAgeAsync(Privacy.DefaultRetention);
        await _store.PruneAsync(Privacy.MaxUnpinnedClips);

        // Seed duplicate suppression with the newest stored clip.
        var recent = await _store.GetRecentAsync(1);
        if (recent.Count > 0) _lastSignature = recent[0].ContentHash;

        _overlay = new OverlayWindow(_store, OverlayCacheSize);
        _overlay.PasteRequested += OnPasteRequested;

        _listener = new ClipboardListener();
        _listener.ClipCaptured += OnClipCaptured;
        try
        {
            _listener.Start();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ClipForge: clipboard listener failed to start: {ex.Message}");
        }

        try
        {
            _hotkeys = new HotkeyService(_overlay);
            _hotkeys.Pressed += (_, _) => _overlay.Toggle();
        }
        catch (Exception ex)
        {
            // Hotkey already taken by another app — keep running, tray can still summon the overlay.
            Debug.WriteLine($"ClipForge: hotkey registration failed: {ex.Message}");
        }

        _tray = new TrayIcon(
            showAction: () => _overlay.Toggle(),
            exitAction: () => Shutdown());

        // Whatever was on the clipboard before launch becomes the first history entry.
        try
        {
            var initial = ClipboardListener.CaptureCurrent();
            if (initial is not null) OnClipCaptured(this, initial);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ClipForge: initial capture failed: {ex.Message}");
        }
    }

    private async void OnClipCaptured(object? sender, CapturedClip captured)
    {
        try
        {
            // Privacy-by-default: password managers and secrets are never stored.
            if (Privacy.IsSensitiveApp(captured.SourceApp)) return;
            if (captured.Type is ClipType.Text or ClipType.Html &&
                SecretDetector.Detect(captured.Text) is not null) return;

            var clip = captured.ToClip();
            if (clip.ContentHash == _lastSignature) return; // duplicate of last copy
            _lastSignature = clip.ContentHash;
            if (_store is null) return;
            await _store.SaveAsync(clip);
            _overlay?.NotifyClipAdded(clip);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ClipForge: capture failed: {ex.Message}");
        }
    }

    private async Task OnPasteRequested(Clip clip)
    {
        _overlay?.HideOverlay();
        try
        {
            // Don't re-capture what we're about to paste.
            using var _ = _listener?.SuppressCapture(TimeSpan.FromSeconds(3));
            await PasteService.PasteAsync(clip);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ClipForge: paste failed: {ex.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _hotkeys?.Dispose();
        _listener?.Dispose();
        base.OnExit(e);
    }
}
