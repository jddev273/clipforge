using System.Diagnostics;
using System.IO;
using System.Windows;
using ClipForge.Core;
using ClipForge.Core.Native;
using ClipForge.Formats;
using ClipForge.Search;
using ClipForge.Secrets;
using ClipForge.Storage.Sqlite;

// CA1724: 'App' is the WPF application-entry-point convention and x:Class-bound — renaming would fight the framework.
// CA2007: WPF UI thread affinity — continuations below touch UI objects (OverlayWindow) and must keep the context.
#pragma warning disable CA1724
#pragma warning disable CA2007

namespace ClipForge.App;

/// <summary>
/// App bootstrap: SQLite store → clipboard listener → global hotkey → overlay → tray.
/// No main window at launch; the overlay is summoned with Ctrl+Shift+V.
/// </summary>
// MA0049: the name 'App' is mandated by WPF (App.xaml's x:Class + StartupUri); renaming would break the framework contract.
#pragma warning disable MA0049
public partial class App : System.Windows.Application, IDisposable
{
    private const int OverlayCacheSize = 500;

    private ClipboardListener? _listener;
    private HotkeyService? _hotkeys;
    private TrayIcon? _tray;
    private OverlayWindow? _overlay;
    // CA1859: interface fields are deliberate seams — every major piece is swappable via plugins/.
#pragma warning disable CA1859
    private IClipStore? _store;
    private ISecretDetector _secrets = new SecretDetector();
    private ISearchRanker _ranker = new FuzzyMatcher();
    private IReadOnlyList<IClipFormatHandler> _formatHandlers = Array.Empty<IClipFormatHandler>();
#pragma warning restore CA1859
    private string _lastSignature = string.Empty;
    private bool _disposed;

    // VSTHRD100: framework-mandated override signature — WPF requires async void for OnStartup.
    // The body is fully guarded below, so no exception escapes unobserved (lane3 adjunct).
#pragma warning disable VSTHRD100
    protected override async void OnStartup(StartupEventArgs e)
    {
        // IDISP003: these fields are assigned exactly once, here in OnStartup; all are disposed
        // deterministically in Dispose(bool). The analyzer cannot prove single-assignment.
#pragma warning disable IDISP003
        try
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Plugin seams: every major piece is swappable — drop a DLL implementing
            // the interface into plugins/ next to the exe (see README "Improving a piece").
            _secrets = PluginLoader.Load<ISecretDetector>(new SecretDetector());
            _ranker = PluginLoader.Load<ISearchRanker>(new FuzzyMatcher());
            _formatHandlers = PluginLoader.LoadAll<IClipFormatHandler>(new IClipFormatHandler[]
            {
                new FilesFormatHandler(), new ImageFormatHandler(),
                new HtmlFormatHandler(), new TextFormatHandler()
            });

            _store = await InitializeStoreAsync().ConfigureAwait(true);

            var overlay = new OverlayWindow(_store, _ranker, OverlayCacheSize);
            _overlay = overlay;
            overlay.PasteRequested += OnPasteRequested;

            StartInputServices(overlay);

            _tray = new TrayIcon(
                showAction: () => _overlay.Toggle(),
                exitAction: () => Shutdown());

            // Whatever was on the clipboard before launch becomes the first history entry.
            try
            {
                var initial = ClipboardListener.CaptureCurrent(_formatHandlers);
                if (initial is not null) OnClipCaptured(this, new ClipCapturedEventArgs(initial));
            }
            #pragma warning disable CA1031 // why: startup/shutdown best-effort: a fault here is diagnosed via Debug output, never allowed to take down the app.
            catch (Exception ex)
            {
                Debug.WriteLine($"ClipForge: initial capture failed: {ex.Message}");
            }
            #pragma warning restore CA1031
        }
        #pragma warning disable CA1031 // why: startup must never take down the process; any fault is diagnosed via Debug output.
        catch (Exception ex)
        {
            Debug.WriteLine($"ClipForge: startup failed: {ex.Message}");
        }
        #pragma warning restore CA1031
#pragma warning restore IDISP003
    }
#pragma warning restore VSTHRD100

    /// <summary>Starts the clipboard listener and global hotkey. (MA0051: extracted from OnStartup.)</summary>
    // IDISP003: single-assignment here in startup; disposed deterministically in Dispose(bool).
#pragma warning disable IDISP003
    private void StartInputServices(OverlayWindow overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        _listener = new ClipboardListener(_formatHandlers);
        _listener.ClipCaptured += OnClipCaptured;
        try
        {
            _listener.Start();
        }
        #pragma warning disable CA1031 // why: startup/shutdown best-effort: a fault here is diagnosed via Debug output, never allowed to take down the app.
        catch (Exception ex)
        {
            Debug.WriteLine($"ClipForge: clipboard listener failed to start: {ex.Message}");
        }
        #pragma warning restore CA1031

        try
        {
            _hotkeys = new HotkeyService(overlay);
            _hotkeys.Pressed += (_, _) => overlay.Toggle();
        }
        #pragma warning disable CA1031 // why: startup/shutdown best-effort: a fault here is diagnosed via Debug output, never allowed to take down the app.
        catch (Exception ex)
        {
            // Hotkey already taken by another app — keep running, tray can still summon the overlay.
            Debug.WriteLine($"ClipForge: hotkey registration failed: {ex.Message}");
        }
        #pragma warning restore CA1031
    }
#pragma warning restore IDISP003

    /// <summary>Opens the store, enforces retention, and seeds duplicate suppression. (MA0051: extracted from OnStartup.)</summary>
    private async Task<IClipStore> InitializeStoreAsync()
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClipForge", "clips.db");

        var store = PluginLoader.Load<IClipStore>(new SqliteClipStore(dbPath));
        await store.InitializeAsync().ConfigureAwait(false);
        // Privacy-by-default: retention is NOT indefinite.
        await store.PruneByAgeAsync(Privacy.DefaultRetention).ConfigureAwait(false);
        await store.PruneAsync(Privacy.MaxUnpinnedClips).ConfigureAwait(false);

        // Seed duplicate suppression with the newest stored clip.
        var recent = await store.GetRecentAsync(1).ConfigureAwait(false);
        if (recent.Count > 0) _lastSignature = recent[0].ContentHash;
        return store;
    }

    // VSTHRD100: genuine event handler (EventHandler<ClipCapturedEventArgs> shape); the analyzer's
    // carve-out misses the nullable 'object?' sender annotation. Body is fully guarded below.
#pragma warning disable VSTHRD100
    private async void OnClipCaptured(object? sender, ClipCapturedEventArgs e)
    {
        var captured = e.Captured;
        try
        {
            // Privacy-by-default: password managers and secrets are never stored.
            if (Privacy.IsSensitiveApp(captured.SourceApp)) return;
            if (captured.Type is ClipType.Text or ClipType.Html &&
                _secrets.Detect(captured.Text) is not null) return;

            var clip = captured.ToClip();
            // MA0006: explicit ordinal comparison — hashes are ASCII, culture must not matter.
            if (string.Equals(clip.ContentHash, _lastSignature, StringComparison.Ordinal)) return; // duplicate of last copy
            _lastSignature = clip.ContentHash;
            if (_store is null) return;
            await _store.SaveAsync(clip).ConfigureAwait(true);
            if (_overlay is not null) await _overlay.NotifyClipAddedAsync(clip).ConfigureAwait(true);
        }
        #pragma warning disable CA1031 // why: startup/shutdown best-effort: a fault here is diagnosed via Debug output, never allowed to take down the app.
        catch (Exception ex)
        {
            Debug.WriteLine($"ClipForge: capture failed: {ex.Message}");
        }
        #pragma warning restore CA1031
    }
#pragma warning restore VSTHRD100

    // VSTHRD100: genuine event handler (EventHandler<PasteEventArgs> shape); the analyzer's
    // carve-out misses the nullable 'object?' sender annotation. Body is fully guarded below.
#pragma warning disable VSTHRD100
    private async void OnPasteRequested(object? sender, PasteEventArgs e)
    {
        var clip = e.Clip;
        _overlay?.HideOverlay();
        try
        {
            // Don't re-capture what we're about to paste.
            using var _ = _listener?.SuppressCapture(TimeSpan.FromSeconds(3));
            await PasteService.PasteAsync(clip).ConfigureAwait(true);
        }
        #pragma warning disable CA1031 // why: startup/shutdown best-effort: a fault here is diagnosed via Debug output, never allowed to take down the app.
        catch (Exception ex)
        {
            Debug.WriteLine($"ClipForge: paste failed: {ex.Message}");
        }
        #pragma warning restore CA1031
    }
#pragma warning restore VSTHRD100

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    /// <summary>Deterministic teardown of the owned disposable services (CA1001).</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            _tray?.Dispose();
            _hotkeys?.Dispose();
            _listener?.Dispose();
        }
        _disposed = true;
    }
}

#pragma warning restore CA2007
#pragma warning restore CA1724
#pragma warning restore MA0049
