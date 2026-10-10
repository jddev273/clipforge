namespace ClipForge.Core;

/// <summary>
/// Privacy-by-default policy: what never gets captured, and how long the rest lives.
/// Opinionated defaults — no options maze.
/// </summary>
public static class Privacy
{
    /// <summary>History retention for unpinned clips. NOT indefinite — by design.</summary>
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(90);

    /// <summary>Absolute cap on unpinned clips, applied after the age-based prune.</summary>
    public const int MaxUnpinnedClips = 5000;

    private static readonly HashSet<string> SensitiveApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "1password", "bitwarden", "keepass", "keepassxc", "dashlane", "lastpass",
        "roboform", "nordpass", "keeper", "enpass", "authy", "winauth",
        "keepassdx", "pwsafe", "passwordsafe", "bitwarden"
    };

    /// <summary>True when the copy came from a password manager / authenticator — never store it.</summary>
    public static bool IsSensitiveApp(string? processName)
    {
        if (string.IsNullOrEmpty(processName)) return false;
        return SensitiveApps.Any(a => processName.Contains(a, StringComparison.OrdinalIgnoreCase));
    }
}
