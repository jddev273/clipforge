using System.Runtime.InteropServices;

namespace ClipForge.Core.Native;

/// <summary>
/// Strongly-typed window handle. Raw <see cref="IntPtr"/>s at the interop boundary
/// conflate windows, global memory, and pointers; an <see cref="Hwnd"/> can only be
/// a window. Crossings back into raw interop go through <see cref="ToIntPtr"/>,
/// so every site where a window handle meets untyped native code is visible.
/// Only handles we OWN (create/destroy) are wrapped; transient values stay raw.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Hwnd(nint Value)
{
    public static readonly Hwnd Null = new(0);

    public bool IsNull => Value == 0;

    /// <summary>Named alternate for the explicit <see cref="IntPtr"/> conversion (CA2225).</summary>
    public IntPtr ToIntPtr() => new(Value);

    /// <summary>Named alternate for the explicit <see cref="Hwnd"/> conversion (CA2225).</summary>
    public static Hwnd FromIntPtr(IntPtr p) => new(p);

    public static explicit operator IntPtr(Hwnd h) => h.ToIntPtr();
    public static explicit operator Hwnd(IntPtr p) => FromIntPtr(p);
}
