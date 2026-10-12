using ClipForge.Core;
using ClipForge.Secrets;

using Xunit;

namespace ClipForge.Tests;

/// <summary>
/// Behavior of the built-in secret detector: one positive per pattern class,
/// plus the negatives that keep ordinary copies storable.
/// </summary>
public sealed class SecretDetectorTests
{
    private static readonly SecretDetector Detector = new();

    [Fact]
    public void DetectPrivateKeyBlockReturnsPrivateKey()
    {
        Assert.Equal(SecretKind.PrivateKey,
            Detector.Detect("-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEA\n-----END RSA PRIVATE KEY-----"));
        Assert.Equal(SecretKind.PrivateKey,
            Detector.Detect("-----BEGIN OPENSSH PRIVATE KEY-----\nb3BlbnNzaA==\n-----END OPENSSH PRIVATE KEY-----"));
    }

    [Fact]
    public void DetectPublicKeyOrCertificateReturnsNull()
    {
        Assert.Null(Detector.Detect("-----BEGIN CERTIFICATE-----\nMIIB\n-----END CERTIFICATE-----"));
        Assert.Null(Detector.Detect("-----BEGIN PUBLIC KEY-----\nMFkw\n-----END PUBLIC KEY-----"));
    }

    [Fact]
    public void DetectPasswordAssignmentReturnsPasswordAssignment()
    {
        Assert.Equal(SecretKind.PasswordAssignment, Detector.Detect("password: hunter2"));
        Assert.Equal(SecretKind.PasswordAssignment, Detector.Detect("Password=Sup3rSecret"));
        Assert.Equal(SecretKind.PasswordAssignment, Detector.Detect("the password: hunter2 (from the vault)"));
    }

    [Fact]
    public void DetectPasswordMentionWithoutAssignmentReturnsNull()
    {
        Assert.Null(Detector.Detect("Your password must contain at least 8 characters"));
        Assert.Null(Detector.Detect("Enter your password in the field below"));
    }

    [Fact]
    public void DetectBearerTokenReturnsApiKey()
    {
        Assert.Equal(SecretKind.ApiKey,
            Detector.Detect("Authorization: Bearer abcdefghij0123456789"));
    }

    [Fact]
    public void DetectShortBearerTokenReturnsNull()
    {
        Assert.Null(Detector.Detect("Authorization: Bearer abc"));
    }

    [Fact]
    public void DetectLabeledSecretReturnsApiKey()
    {
        Assert.Equal(SecretKind.ApiKey, Detector.Detect("api_key: abcdefghijklmnop"));
        Assert.Equal(SecretKind.ApiKey, Detector.Detect("access_token = abcdefghijklmnop"));
        Assert.Equal(SecretKind.ApiKey, Detector.Detect("client_secret: \"abcdefghijklmnop\""));
    }

    [Fact]
    public void DetectLabeledSecretWithShortValueReturnsNull()
    {
        Assert.Null(Detector.Detect("api_key: short"));
    }

    [Theory]
    [InlineData("sk-proj-1234567890abcdef")]
    [InlineData("ghp_1234567890abcdef")]
    [InlineData("AKIAIOSFODNN7EXAMPLE")]
    [InlineData("AIzaSyA1234567890abcdefghij")]
    [InlineData("xoxb-1234-567890-abcdEFGH")]
    public void DetectKnownKeyPrefixWithTokenBodyReturnsApiKey(string text)
    {
        Assert.Equal(SecretKind.ApiKey, Detector.Detect(text));
    }

    [Fact]
    public void DetectKeyPrefixWithoutTokenBodyReturnsNull()
    {
        // The prefix alone is not enough: a plausible token body must follow.
        Assert.Null(Detector.Detect("sk-live"));
        Assert.Null(Detector.Detect("the task-force multiplier"));
    }

    [Theory]
    [InlineData("4242 4242 4242 4242")]
    [InlineData("4242424242424242")]
    [InlineData("378282246310005")] // Amex test number, Luhn-valid
    public void DetectLuhnValidCardReturnsCardNumber(string text)
    {
        Assert.Equal(SecretKind.CardNumber, Detector.Detect(text));
    }

    [Fact]
    public void DetectLuhnInvalidCardReturnsNull()
    {
        // Same shape as the valid Visa test number, wrong check digit.
        Assert.Null(Detector.Detect("4242 4242 4242 4241"));
    }

    [Fact]
    public void DetectShortDigitRunReturnsNull()
    {
        Assert.Null(Detector.Detect("call 555 123 4567"));
    }

    [Theory]
    [InlineData("Your verification code is 482916")]
    [InlineData("Your code is 1234")]
    [InlineData("the code is 9876")]
    [InlineData("security code: 731205")]
    public void DetectOtpWithContextReturnsOtpCode(string text)
    {
        Assert.Equal(SecretKind.OtpCode, Detector.Detect(text));
    }

    [Fact]
    public void DetectCodeWithoutSecretContextReturnsNull()
    {
        Assert.Null(Detector.Detect("the code is open source"));
        Assert.Null(Detector.Detect("Dial 123456 for support"));
    }

    [Fact]
    public void DetectBarePasswordReturnsNull()
    {
        // Known limitation (2026-10-12 human test): an unlabeled password has no
        // pattern — no prefix, no assignment, not Luhn/OTP-shaped — so the
        // detector cannot see it. Password-manager exclusion is the control
        // for those; this test pins the boundary so it is never a silent gap.
        Assert.Null(Detector.Detect("Xq9!zvT2mw"));
    }

    [Fact]
    public void DetectOrdinaryTextReturnsNull()
    {
        Assert.Null(Detector.Detect("Quarterly report draft — revenue grew 12% this year."));
        Assert.Null(Detector.Detect("Meeting notes: discuss the roadmap and the hiring plan."));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DetectNullOrWhitespaceReturnsNull(string? text)
    {
        Assert.Null(Detector.Detect(text));
    }

    [Fact]
    public void DetectSecretInsideScanWindowReturnsPasswordAssignment()
    {
        string text = new string('a', 8000) + " password: hunter2";
        Assert.Equal(SecretKind.PasswordAssignment, Detector.Detect(text));
    }

    [Fact]
    public void DetectSecretBeyondScanWindowReturnsNull()
    {
        // Only the first 8KB is scanned (documented best-effort bound).
        string text = new string('a', 8300) + " password: hunter2";
        Assert.Null(Detector.Detect(text));
    }
}
