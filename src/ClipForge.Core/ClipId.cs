namespace ClipForge.Core;

/// <summary>
/// Strongly-typed clip identity. A raw <c>long</c> is confusable with counts, limits,
/// or timestamps; a <see cref="ClipId"/> can only ever identify a clip.
/// </summary>
public readonly record struct ClipId(long Value)
{
    /// <summary>No clip — the value of <see cref="Clip.Id"/> before the store assigns one.</summary>
    public static readonly ClipId None = new(0);

    public bool IsNone => Value == 0;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
