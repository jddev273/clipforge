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
    private AppSettings _settings = new();
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
                new FilesFormatHandler(), new ImageFormatHandler(), new RtfFormatHandler(),
                new HtmlFormatHandler(), new TextFormatHandler()
            });
            _settings = AppSettings.Load();

            _store = await InitializeStoreAsync().ConfigureAwait(true);

            var overlay = new OverlayWindow(_store, _ranker, OverlayCacheSize);
            _overlay = overlay;
            overlay.PasteRequested += OnPasteRequested;

            StartInputServices(overlay);

            _tray = new TrayIcon(
                showAction: () => _overlay.Toggle(),
                settingsAction: OpenSettings,
                exitAction: () => Shutdown());

            // Autostart: on by default from the first run; the settings toggle owns it afterwards.
            EnsureAutostart(_settings);

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
            // The listener's message window owns the clipboard whenever this
            // process opens it (paste staging, snapshot restore, reads).
            WindowsClipboard.OwnerWindowHandle = _listener.WindowHandle;
        }
        #pragma warning disable CA1031 // why: startup/shutdown best-effort: a fault here is diagnosed via Debug output, never allowed to take down the app.
        catch (Exception ex)
        {
            Debug.WriteLine($"ClipForge: clipboard listener failed to start: {ex.Message}");
        }
        #pragma warning restore CA1031

        try
        {
            _hotkeys = StartHotkey(overlay, _settings.Hotkey);
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

    /// <summary>Creates and wires the global hotkey. Throws when the chord is invalid or taken.</summary>
    private static HotkeyService StartHotkey(OverlayWindow overlay, string chordText)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        if (!HotkeyChord.TryParse(chordText, out var chord) || chord is null)
            throw new InvalidOperationException($"Invalid hotkey chord in settings: {chordText}");
        var service = new HotkeyService(overlay, chord);
        service.Pressed += (_, _) => overlay.Toggle();
        return service;
    }

    /// <summary>Best-effort first-run autostart enablement. Registry failures never affect startup.</summary>
    private static void EnsureAutostart(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        try
        {
            if (settings.Autostart && !AutostartManager.IsEnabled())
                AutostartManager.SetEnabled(true);
        }
        #pragma warning disable CA1031 // why: autostart is best-effort; a registry failure must not affect startup.
        catch (Exception ex)
        {
            Debug.WriteLine($"ClipForge: autostart setup failed: {ex.Message}");
        }
        #pragma warning restore CA1031
    }

    private void OpenSettings()
    {
        var window = new SettingsWindow(_settings) { Owner = _overlay };
        if (window.ShowDialog() == true && window.Result is not null)
            ApplySettings(window.Result);
    }

    private void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string previousChordText = _settings.Hotkey;
        _settings = settings;
        settings.Save();

        RebindHotkey(previousChordText, settings.Hotkey);

        try
        {
            AutostartManager.SetEnabled(settings.Autostart);
        }
        #pragma warning disable CA1031 // why: best-effort; the checkbox reflects intent even if the registry write failed.
        catch (Exception ex)
        {
            Debug.WriteLine($"ClipForge: autostart toggle failed: {ex.Message}");
        }
        #pragma warning restore CA1031
    }

    /// <summary>
    /// Applies a hotkey change from a settings save. The old and new services share
    /// one (HWND, ID) registration slot, so the old service must be unregistered
    /// BEFORE the new one registers — registering first and disposing after
    /// unregisters the new chord too (the "hotkey dies on any settings save" bug).
    /// An unchanged chord leaves the live registration untouched; a failed
    /// registration rolls back to the previous chord.
    /// </summary>
    private void RebindHotkey(string previousChordText, string newChordText)
    {
        if (_overlay is null) return;
        if (_hotkeys is not null && ChordsEqual(previousChordText, newChordText)) return;

        _hotkeys?.Dispose();
        _hotkeys = null;
        try
        {
            _hotkeys = StartHotkey(_overlay, newChordText);
        }
        #pragma warning disable CA1031 // why: a taken/invalid chord rolls back to the previous hotkey; Debug-logged.
        catch (Exception ex)
        {
            Debug.WriteLine($"ClipForge: hotkey rebind failed, restoring previous: {ex.Message}");
            RestoreHotkey(previousChordText);
        }
        #pragma warning restore CA1031
    }

    /// <summary>
    /// Rollback for a failed rebind: re-register the previous chord and revert the
    /// persisted setting, so a restart doesn't retry the taken chord. The user is
    /// told with the settings error dialog (same caption/icon as SettingsWindow).
    /// </summary>
    private void RestoreHotkey(string previousChordText)
    {
        if (_overlay is null) return;
        try
        {
            // IDISP003: RebindHotkey disposed and nulled _hotkeys immediately before
            // calling this rollback; the analyzer cannot prove that across methods.
#pragma warning disable IDISP003
            _hotkeys = StartHotkey(_overlay, previousChordText);
#pragma warning restore IDISP003
            _settings.Hotkey = previousChordText;
            _settings.Save();
            MessageBox.Show(
                $"That hotkey is already used by another app, so the previous hotkey ({previousChordText}) stays in effect.",
                "ClipForge Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        #pragma warning disable CA1031 // why: if even the previous chord can't be re-registered, the tray icon still summons the overlay; Debug-logged.
        catch (Exception ex)
        {
            Debug.WriteLine($"ClipForge: hotkey rollback failed: {ex.Message}");
        }
        #pragma warning restore CA1031
    }

    /// <summary>Same chord = same Win32 registration: equal modifiers + key.</summary>
    private static bool ChordsEqual(string a, string b) =>
        HotkeyChord.TryParse(a, out var chordA) && chordA is not null &&
        HotkeyChord.TryParse(b, out var chordB) && chordB is not null &&
        chordA.ToWin32() == chordB.ToWin32();

    /// <summary>Opens the store, enforces retention, and seeds duplicate suppression. (MA0051: extracted from OnStartup.)</summary>
    private async Task<IClipStore> InitializeStoreAsync()
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClipForge", "clips.db");

        var store = PluginLoader.Load<IClipStore>(new SqliteClipStore(dbPath));
        await store.InitializeAsync().ConfigureAwait(false);
        // Privacy-by-default: retention is NOT indefinite (user-configurable, 90 days default).
        await store.PruneByAgeAsync(TimeSpan.FromDays(_settings.RetentionDays)).ConfigureAwait(false);
        await store.PruneAsync(_settings.MaxUnpinnedClips).ConfigureAwait(false);

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
            // Privacy-by-default: password managers, user-excluded apps and detected
            // secrets are refused at the capture boundary (best-effort filter, not a guarantee).
            if (Privacy.IsSensitiveApp(captured.SourceApp) || _settings.IsExcludedApp(captured.SourceApp)) return;
            if (captured.Type is ClipType.Text or ClipType.Html or ClipType.Rtf &&
                _secrets.Detect(captured.SecretScanText) is not null) return;
            // A text capture with no readable text (whitespace-only copy, empty
            // RTF paragraph) would land as a blank history row — refuse it too.
            if (captured.IsEmptyTextCapture) return;

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
            if (e.PlainText)
                await PasteService.PastePlainTextAsync(clip).ConfigureAwait(true);
            else
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
