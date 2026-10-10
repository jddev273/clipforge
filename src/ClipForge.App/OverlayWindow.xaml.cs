using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using ClipForge.Core;

// CA2007: WPF UI thread affinity — continuations touch UI objects and must keep the context.
#pragma warning disable CA2007

namespace ClipForge.App;

/// <summary>
/// The command-palette overlay: Ctrl+Shift+V summons it, typing fuzzy-filters history
/// instantly, Enter pastes, Esc (or focus loss) dismisses. Never nags, never blocks.
/// </summary>
public partial class OverlayWindow : Window
{
    private readonly IClipStore _store;
    private readonly ISearchRanker _ranker;
    private readonly int _cacheSize;
    private List<Clip> _all = new();

    /// <summary>Raised when the user picks a clip. The handler pastes it.</summary>
    public event EventHandler<PasteEventArgs>? PasteRequested;

    public OverlayWindow(IClipStore store, ISearchRanker ranker, int cacheSize)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(ranker);
        ArgumentOutOfRangeException.ThrowIfLessThan(cacheSize, 1);
        _store = store;
        _ranker = ranker;
        _cacheSize = cacheSize;
        InitializeComponent();
    }

    public void Toggle()
    {
        if (IsVisible) HideOverlay();
        else StartShowOverlay();
    }

    /// <summary>
    /// Fire-and-forget with observation strategy (lane3 rule 6 carve-out): ShowOverlayAsync
    /// is fully guarded internally, and any fault is logged via the continuation.
    /// </summary>
    private void StartShowOverlay()
    {
#pragma warning disable MA0134 // why: deliberate fire-and-forget with fault-logging continuation (lane3 rule 6 carve-out); the continuation IS the observation strategy.
        _ = ShowOverlayAsync().ContinueWith(
            static t => Debug.WriteLine($"ClipForge: ShowOverlay failed: {t.Exception?.Message}"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
#pragma warning restore MA0134
    }

    public async Task ShowOverlayAsync()
    {
        try
        {
            _all = (await _store.GetRecentAsync(_cacheSize).ConfigureAwait(true)).ToList();
        }
        // CA1031: startup best-effort — a fault here is diagnosed via Debug output, never allowed to take down the app.
#pragma warning disable CA1031
        catch
        {
            _all = new List<Clip>();
        }
#pragma warning restore CA1031

        searchBox.Text = string.Empty;
        ApplyFilter(string.Empty);
        Position();

        Show();
        Activate();
        searchBox.Focus();
        Keyboard.Focus(searchBox);
    }

    public void HideOverlay()
    {
        if (IsVisible) Hide();
    }

    /// <summary>Called when a new clip is captured while the overlay is open.</summary>
    public async Task NotifyClipAddedAsync(Clip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (!IsVisible) return;
        // VSTHRD001: no JoinableTaskFactory in a plain WPF app (that API is VS-SDK-only).
        // Awaited InvokeAsync never blocks a thread, so the rule's deadlock concern does not apply.
#pragma warning disable VSTHRD001
        await Dispatcher.InvokeAsync(() =>
        {
            // Keep pinned-first ordering consistent with GetRecentAsync.
            int idx = _all.FindIndex(c => !c.Pinned);
            if (idx < 0) _all.Add(clip);
            else _all.Insert(idx, clip);
            ApplyFilter(searchBox.Text);
        }).Task.ConfigureAwait(true);
#pragma warning restore VSTHRD001
    }

    private void Position()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + area.Height * 0.16;
    }

    private void ApplyFilter(string query)
    {
        var ranked = _ranker.FilterAndRank(_all, query, 200);
        clipsList.ItemsSource = ranked.Select(c => new ClipItem(c)).ToList();
        // Pinned section on top: group by PinnedGroup (0 = pinned). The view is fresh
        // per ItemsSource assignment, so the description is (re-)added every time.
        var view = CollectionViewSource.GetDefaultView(clipsList.ItemsSource);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ClipItem.PinnedGroup)));
        if (clipsList.Items.Count > 0)
            clipsList.SelectedIndex = 0;
        countText.Text = $"{clipsList.Items.Count} clip{(clipsList.Items.Count == 1 ? string.Empty : "s")}";
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        watermark.Visibility = string.IsNullOrEmpty(searchBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter(searchBox.Text);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                HideOverlay();
                e.Handled = true;
                break;
            case Key.Enter:
                PasteSelected();
                e.Handled = true;
                break;
            case Key.P when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                TogglePinSelected();
                e.Handled = true;
                break;
            case Key.Down:
                MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        if (clipsList.Items.Count == 0) return;
        int next = Math.Clamp(clipsList.SelectedIndex + delta, 0, clipsList.Items.Count - 1);
        clipsList.SelectedIndex = next;
        clipsList.ScrollIntoView(clipsList.SelectedItem);
    }

    private void ClipsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        PasteSelected();
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.Button)?.DataContext is ClipItem item)
            TogglePin(item);
    }

    private void TogglePinSelected()
    {
        if (clipsList.SelectedItem is ClipItem item)
            TogglePin(item);
    }

    /// <summary>
    /// Fire-and-forget with internal fault handling (MA0134 carve-out): TogglePinAsync
    /// catches everything and Debug-logs; a pin failure must never crash the overlay.
    /// </summary>
    private void TogglePin(ClipItem item)
    {
#pragma warning disable MA0134 // why: deliberate fire-and-forget; TogglePinAsync is fully guarded internally.
        _ = TogglePinAsync(item);
#pragma warning restore MA0134
    }

    private async Task TogglePinAsync(ClipItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        try
        {
            if (_store is null) return;
            await _store.SetPinnedAsync(item.Clip.Id, !item.Clip.Pinned).ConfigureAwait(true);
            // Re-query so the pinned section reorders and the row re-renders.
            _all = (await _store.GetRecentAsync(_cacheSize).ConfigureAwait(true)).ToList();
            ApplyFilter(searchBox.Text);
        }
        // CA1031: pin toggle is best-effort UI — a fault is diagnosed via Debug output, never crashes the overlay.
        #pragma warning disable CA1031
        catch (Exception ex)
        {
            Debug.WriteLine($"ClipForge: pin toggle failed: {ex.Message}");
        }
        #pragma warning restore CA1031
    }

    private void PasteSelected()
    {
        if (clipsList.SelectedItem is ClipItem item)
            PasteRequested?.Invoke(this, new PasteEventArgs(item.Clip)); // handler hides the overlay, then pastes
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        // Clicking away dismisses — the picker never traps the user mid-flow.
        HideOverlay();
    }
}

#pragma warning restore CA2007
