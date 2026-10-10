namespace ClipForge.Core;

/// <summary>
/// Privacy seam: detects secrets in copied text so they are NEVER stored.
/// Implement this interface and drop the DLL in plugins/ to replace the detector.
/// </summary>
public interface ISecretDetector
{
    /// <summary>Returns the kind of secret found, or null when the text looks safe to store.</summary>
    SecretKind? Detect(string? text);
}
