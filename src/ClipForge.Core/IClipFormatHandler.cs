namespace ClipForge.Core;

/// <summary>
/// Format seam: one handler per clipboard format. Handlers are tried in
/// <see cref="Priority"/> order; the first that both claims and captures wins.
/// Implement this interface and drop the DLL in plugins/ to add or replace a format.
/// </summary>
public interface IClipFormatHandler
{
    /// <summary>Human name, e.g. "Text". Used in diagnostics.</summary>
    string Name { get; }

    /// <summary>Lower runs first. Built-ins: Files=0, Image=10, Html=20, Text=30.</summary>
    int Priority { get; }

    /// <summary>True when this format is currently on the clipboard. Must be cheap.</summary>
    bool CanHandle();

    /// <summary>Reads the format. Null means transient failure — the caller retries once.</summary>
    CapturedClip? Capture();
}
