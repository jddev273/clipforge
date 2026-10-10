using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace ClipForge.App;

/// <summary>Minimal settings UI. No options maze — five controls, that's the whole surface.</summary>
public partial class SettingsWindow : Window
{
    private static readonly string[] NewLineSeparators = ["\r\n", "\n"];
    private HotkeyChord? _pendingChord;
    private bool _capturing;

    /// <summary>The validated settings, or null when the user cancelled.</summary>
    public AppSettings? Result { get; private set; }

    public SettingsWindow(AppSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);
        InitializeComponent();
        retentionBox.Text = current.RetentionDays.ToString(CultureInfo.InvariantCulture);
        maxClipsBox.Text = current.MaxUnpinnedClips.ToString(CultureInfo.InvariantCulture);
        excludedBox.Text = string.Join(Environment.NewLine, current.ExcludedApps);
        _pendingChord = HotkeyChord.TryParse(current.Hotkey, out var chord) ? chord : null;
        hotkeyBox.Text = _pendingChord?.ToString() ?? current.Hotkey;
        autostartCheck.IsChecked = current.Autostart;
    }

    private void ChangeHotkey_Click(object sender, RoutedEventArgs e)
    {
        _capturing = true;
        hotkeyBox.Text = string.Empty;
        hotkeyHint.Text = "Press the new hotkey chord… (Esc cancels)";
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturing) return;
        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            _capturing = false;
            hotkeyHint.Text = string.Empty;
            hotkeyBox.Text = _pendingChord?.ToString() ?? string.Empty;
            return;
        }
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        // Wait for the non-modifier key: bare Ctrl/Alt/Shift presses are not chords yet.
        if (IsModifierKey(key)) return;
        HotkeyChord chord;
        try
        {
            chord = new HotkeyChord(Keyboard.Modifiers, key);
        }
        catch (ArgumentException)
        {
            hotkeyHint.Text = "That needs at least one modifier (Ctrl / Alt / Shift / Win).";
            return;
        }
        _pendingChord = chord;
        _capturing = false;
        hotkeyHint.Text = string.Empty;
        hotkeyBox.Text = chord.ToString();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(retentionBox.Text, CultureInfo.InvariantCulture, out int days) || days is < 1 or > 3650)
        {
            ShowError("Retention days must be between 1 and 3650.");
            return;
        }
        if (!int.TryParse(maxClipsBox.Text, CultureInfo.InvariantCulture, out int max) || max is < 100 or > 100000)
        {
            ShowError("Max unpinned clips must be between 100 and 100000.");
            return;
        }
        if (_pendingChord is null)
        {
            ShowError("Set a hotkey first (click Change… and press the chord).");
            return;
        }
        var result = new AppSettings
        {
            RetentionDays = days,
            MaxUnpinnedClips = max,
            Hotkey = _pendingChord.ToString(),
            Autostart = autostartCheck.IsChecked == true,
        };
        foreach (string app in excludedBox.Text
                     .Split(NewLineSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            result.ExcludedApps.Add(app);
        Result = result;
        DialogResult = true;
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

    private static void ShowError(string message) =>
        MessageBox.Show(message, "ClipForge Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
}
