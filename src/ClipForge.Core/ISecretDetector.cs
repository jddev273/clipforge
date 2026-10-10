namespace ClipForge.Core;

/// <summary>
/// Privacy seam: best-effort detection of secrets in copied text so matches are refused
/// at the capture boundary. Implement this interface and drop the DLL in plugins/ to replace the detector.
/// </summary>
public interface ISecretDetector
{
    /// <summary>Returns the kind of secret found, or null when the text looks safe to store.</summary>
    SecretKind? Detect(string? text);
}
