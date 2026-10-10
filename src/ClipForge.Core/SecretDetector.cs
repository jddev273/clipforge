using System.Text.RegularExpressions;

namespace ClipForge.Core;

/// <summary>
/// Privacy-by-default: detects secrets in copied text so they are NEVER stored.
/// Deterministic pattern matching (no ML, no network). When in doubt it errs on the
/// side of not storing — a missed history entry beats a leaked credential.
/// </summary>
public static class SecretDetector
{
    public enum SecretKind
    {
        PrivateKey,
        PasswordAssignment,
        ApiKey,
        CardNumber,
        OtpCode
    }

    private static readonly Regex PasswordAssign =
        new(@"\bpasswou?rd\b\s*[:=]\s*\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BearerToken =
        new(@"\bbearer\s+[A-Za-z0-9\-._~+/]{20,}={0,2}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LabeledSecret =
        new(@"\b(api[_-]?key|api[_-]?secret|client[_-]?secret|access[_-]?token|auth[_-]?token)\b\s*[:=]\s*[""']?([\w\-.~+/]{16,})[""']?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] KeyPrefixes =
    {
        "sk-", "ghp_", "gho_", "ghu_", "ghs_", "github_pat_", "AKIA", "ASIA",
        "xoxb-", "xoxp-", "xoxa-", "xoxs-", "xoxo-", "AIza", "sq0atp-", "rk_live_"
    };

    private static readonly Regex CardCandidate =
        new(@"\b\d(?:[ \-]?\d){11,18}\b", RegexOptions.Compiled);

    private static readonly Regex OtpContext =
        new(@"(?i)(otp|2fa|two[ -]?factor|verification|verify|security)\W{0,30}code\W{0,10}(\d{4,8})\b|" +
            @"(?i)\b(your|the)\s+(verification\s+)?code\s+(is\s+)?(\d{4,8})\b",
            RegexOptions.Compiled);

    /// <summary>Returns the kind of secret found, or null when the text looks safe to store.</summary>
    public static SecretKind? Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        // Keep it cheap: most copies are short; cap the scan window.
        string scan = text.Length > 8192 ? text[..8192] : text;

        if (scan.Contains("-----BEGIN ", StringComparison.Ordinal) &&
            scan.Contains("PRIVATE KEY-----", StringComparison.Ordinal))
            return SecretKind.PrivateKey;

        if (PasswordAssign.IsMatch(scan))
            return SecretKind.PasswordAssignment;

        if (BearerToken.IsMatch(scan) || LabeledSecret.IsMatch(scan))
            return SecretKind.ApiKey;

        foreach (var prefix in KeyPrefixes)
        {
            int i = scan.IndexOf(prefix, StringComparison.Ordinal);
            if (i >= 0 && HasTokenBody(scan, i + prefix.Length))
                return SecretKind.ApiKey;
        }

        foreach (Match m in CardCandidate.Matches(scan))
        {
            var digits = new string(m.Value.Where(char.IsDigit).ToArray());
            if (digits.Length is >= 13 and <= 19 && IsLuhnValid(digits))
                return SecretKind.CardNumber;
        }

        if (OtpContext.IsMatch(scan))
            return SecretKind.OtpCode;

        return null;
    }

    private static bool HasTokenBody(string s, int start)
    {
        int len = 0;
        for (int i = start; i < s.Length && len < 64; i++, len++)
        {
            char c = s[i];
            if (!(char.IsLetterOrDigit(c) || c is '-' or '_' or '.')) break;
        }
        return len >= 16;
    }

    private static bool IsLuhnValid(string digits)
    {
        int sum = 0;
        bool doubleIt = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int d = digits[i] - '0';
            if (doubleIt)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
            doubleIt = !doubleIt;
        }
        return sum % 10 == 0;
    }
}
