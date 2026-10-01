using System.Text.RegularExpressions;

namespace ClipStack.Core;

/// <summary>
/// Local, heuristic detection of secrets in copied text. Runs entirely offline.
/// Biased towards precision: a false positive silently drops something the user wanted.
/// </summary>
public static partial class SensitiveDetector
{
    public static string? Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();

        if (PrivateKey().IsMatch(t)) return "private key";
        if (KnownToken().IsMatch(t)) return "API token";
        if (t.Length > 512) return null; // the remaining checks target short, single values

        if (Jwt().IsMatch(t)) return "JWT";
        if (IsCreditCard(t)) return "credit card number";
        if (OtpCode().IsMatch(t)) return "authentication code";
        if (LooksLikeSecretKey(t)) return "secret key";
        if (LooksLikePassword(t)) return "password";
        return null;
    }

    [GeneratedRegex(@"-----BEGIN (?:[A-Z]+ )*PRIVATE KEY(?: BLOCK)?-----")]
    private static partial Regex PrivateKey();

    [GeneratedRegex(@"(?:^|[^A-Za-z0-9])(?:AKIA[0-9A-Z]{16}|gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{40,}|glpat-[A-Za-z0-9_\-]{20,}|xox[abprs]-[A-Za-z0-9-]{10,}|sk-(?:ant-|proj-)?[A-Za-z0-9_\-]{20,}|(?:sk|rk)_live_[A-Za-z0-9]{20,}|AIza[0-9A-Za-z_\-]{35}|npm_[A-Za-z0-9]{36})")]
    private static partial Regex KnownToken();

    [GeneratedRegex(@"^eyJ[A-Za-z0-9_-]{5,}\.eyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{10,}$")]
    private static partial Regex Jwt();

    [GeneratedRegex(@"^\d{6}$|^\d{3}[ -]\d{3}$|^\d{8}$")]
    private static partial Regex OtpCode();

    [GeneratedRegex(@"^(?:\d[ -]?){12,18}\d$")]
    private static partial Regex CardShape();

    internal static bool IsCreditCard(string t)
    {
        if (!CardShape().IsMatch(t)) return false;
        var digits = t.Where(char.IsDigit).Select(c => c - '0').ToArray();
        if (digits.Length is < 13 or > 19) return false;
        if (digits.Distinct().Count() < 2) return false; // e.g. 0000 0000 0000 0000
        int sum = 0;
        for (int i = 0; i < digits.Length; i++)
        {
            int d = digits[digits.Length - 1 - i];
            if (i % 2 == 1) { d *= 2; if (d > 9) d -= 9; }
            sum += d;
        }
        return sum % 10 == 0;
    }

    /// <summary>Long random-looking token (mixed case + digits, high entropy), e.g. generic API secrets.</summary>
    internal static bool LooksLikeSecretKey(string t)
    {
        if (t.Length < 32 || t.Length > 256) return false;
        if (!t.All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '=' or '_' or '-')) return false;
        if (!(t.Any(char.IsAsciiLetterLower) && t.Any(char.IsAsciiLetterUpper) && t.Any(char.IsAsciiDigit))) return false;
        return Entropy(t) >= 4.2;
    }

    /// <summary>Single short token mixing character classes, the way generated passwords do.</summary>
    internal static bool LooksLikePassword(string t)
    {
        if (t.Length is < 8 or > 64) return false;
        if (t.Any(char.IsWhiteSpace)) return false;
        if (t.Contains('/') || t.Contains('\\') || t.Contains("://") || t.Contains('@') && t.Contains('.')) return false;
        if (t.Contains('(') || t.Contains('<') || t.Contains('{')) return false; // code-ish

        int classes = 0;
        if (t.Any(char.IsAsciiLetterLower)) classes++;
        if (t.Any(char.IsAsciiLetterUpper)) classes++;
        if (t.Any(char.IsAsciiDigit)) classes++;
        bool symbol = t.Any(c => !char.IsAsciiLetterOrDigit(c));
        if (symbol) classes++;

        // Generated passwords mix all four classes; identifiers like "HttpClient2" or "my_var1" don't
        // usually include a symbol *and* both cases *and* digits, and if they do they are low-entropy.
        return classes == 4 && Entropy(t) >= 3.0 && !LooksLikeIdentifier(t) && LongestLetterRun(t) < 7;
    }

    /// <summary>Real words ("Collections", "Summer") are rare in generated passwords but common in code and prose.</summary>
    private static int LongestLetterRun(string t)
    {
        int best = 0, run = 0;
        foreach (var c in t)
        {
            run = char.IsAsciiLetter(c) ? run + 1 : 0;
            best = Math.Max(best, run);
        }
        return best;
    }

    private static bool LooksLikeIdentifier(string t) =>
        t.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-' or ':') &&
        t.Count(c => c is '_' or '.' or '-' or ':') >= 2;

    internal static double Entropy(string s)
    {
        var counts = new Dictionary<char, int>();
        foreach (var c in s) counts[c] = counts.GetValueOrDefault(c) + 1;
        double e = 0;
        foreach (var n in counts.Values)
        {
            double p = (double)n / s.Length;
            e -= p * Math.Log2(p);
        }
        return e;
    }
}
