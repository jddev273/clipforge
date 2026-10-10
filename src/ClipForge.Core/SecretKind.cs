namespace ClipForge.Core;

/// <summary>Kinds of secrets a detector recognizes.</summary>
public enum SecretKind
{
    PrivateKey,
    PasswordAssignment,
    ApiKey,
    CardNumber,
    OtpCode
}
