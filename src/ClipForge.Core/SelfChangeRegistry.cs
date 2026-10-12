namespace ClipForge.Core;

/// <summary>
/// Identity-based capture suppression for the clipboard changes ClipForge
/// causes itself. Before pasting, the app registers the content hashes of the
/// changes it is about to cause — the staged clip, and the pre-paste snapshot
/// it will restore afterwards. When a capture arrives, a hash matching a
/// registration marks the change as ours: it is dropped, and the registration
/// is consumed (one registration suppresses exactly one capture). Anything
/// else is a genuine copy and is captured normally — including copies made
/// while a paste is in flight, which the previous fixed time-window
/// suppression discarded wholesale: it could not tell our changes from the
/// user's, so every copy inside the window was lost to history.
///
/// A genuine re-copy of content identical to a registered change is dropped
/// too. That is indistinguishable from the app's duplicate-of-last-capture
/// behavior, so it introduces no new false-positive class.
///
/// Registrations are time-boxed purely as a leak bound: if a registered
/// change is never captured (its update was missed entirely), the entry
/// expires instead of lingering to suppress some much later identical copy.
/// The paste operation itself takes under a second; the lifetime below is
/// deliberately far longer than that and far shorter than a human's
/// re-copy cadence.
/// </summary>
public sealed class SelfChangeRegistry
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(10);

    private readonly object _gate = new();
    private readonly List<(string Hash, DateTimeOffset ExpiresAt)> _pending = new();
    private readonly TimeSpan _lifetime;
    private readonly Func<DateTimeOffset> _clock;

    public SelfChangeRegistry()
        : this(DefaultLifetime, null)
    {
    }

    public SelfChangeRegistry(TimeSpan lifetime, Func<DateTimeOffset>? clock = null)
    {
        if (lifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Registration lifetime must be positive.");
        _lifetime = lifetime;
        _clock = clock ?? (static () => DateTimeOffset.UtcNow);
    }

    /// <summary>Registers one expected self-change. Each registration suppresses one matching capture.</summary>
    public void Register(string contentHash)
    {
        ArgumentNullException.ThrowIfNull(contentHash);
        lock (_gate)
        {
            PruneExpired();
            _pending.Add((contentHash, _clock() + _lifetime));
        }
    }

    /// <summary>
    /// If <paramref name="contentHash"/> matches a live registration, consumes
    /// one and returns true (the capture is one of our own changes — drop it).
    /// Otherwise returns false and leaves every registration intact.
    /// </summary>
    public bool TryConsume(string contentHash)
    {
        ArgumentNullException.ThrowIfNull(contentHash);
        lock (_gate)
        {
            PruneExpired();
            // MA0006: explicit ordinal comparison — hashes are ASCII, culture must not matter.
            int i = _pending.FindIndex(e => string.Equals(e.Hash, contentHash, StringComparison.Ordinal));
            if (i < 0) return false;
            _pending.RemoveAt(i);
            return true;
        }
    }

    private void PruneExpired()
    {
        var now = _clock();
        _pending.RemoveAll(e => e.ExpiresAt <= now);
    }
}
