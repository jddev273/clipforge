using System.Diagnostics;
using System.IO;
using System.Text.Json;
using ClipForge.Core;

namespace ClipForge.App;

/// <summary>User settings, persisted as JSON in %LOCALAPPDATA%/ClipForge. Minimal by design — no options maze.</summary>
public sealed class AppSettings
{
    // Defaults come from the Privacy policy (single source of truth); the user can loosen or tighten.
    public int RetentionDays { get; set; } = (int)Privacy.DefaultRetention.TotalDays;
    public int MaxUnpinnedClips { get; set; } = Privacy.MaxUnpinnedClips;
    // Get-only IList: STJ populates it via IList.Add (satisfies CA2227/CA1002/MA0016 together).
    public IList<string> ExcludedApps { get; } = new List<string>();
    public string Hotkey { get; set; } = "Ctrl+Shift+V";
    public bool Autostart { get; set; } = true;

    /// <summary>True when the user excluded this process name (on top of the built-in password-manager list).</summary>
    public bool IsExcludedApp(string? processName)
    {
        if (string.IsNullOrEmpty(processName)) return false;
        return ExcludedApps.Any(a => processName.Contains(a, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClipForge", "settings.json");

    /// <summary>Loads settings; a missing or corrupt file falls back to defaults.</summary>
    public static AppSettings Load()
    {
        try
        {
            string path = FilePath;
            if (!File.Exists(path)) return new AppSettings();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
            if (settings is null) return new AppSettings();
            settings.Hotkey = string.IsNullOrWhiteSpace(settings.Hotkey) ? "Ctrl+Shift+V" : settings.Hotkey;
            return settings;
        }
        // CA1031: a corrupt settings file must degrade to defaults, never crash startup.
        #pragma warning disable CA1031
        catch (Exception ex)
        {
            Debug.WriteLine($"ClipForge: settings load failed, using defaults: {ex.Message}");
            return new AppSettings();
        }
        #pragma warning restore CA1031
    }

    /// <summary>Persists settings. Best-effort: a failure is Debug-logged, never thrown.</summary>
    public void Save()
    {
        try
        {
            string path = FilePath;
            string? dir = Path.GetDirectoryName(path);
            if (dir is not null) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(this, SerializerOptions));
        }
        #pragma warning disable CA1031 // why: settings persistence is best-effort by design.
        catch (Exception ex)
        {
            Debug.WriteLine($"ClipForge: settings save failed: {ex.Message}");
        }
        #pragma warning restore CA1031
    }
}
