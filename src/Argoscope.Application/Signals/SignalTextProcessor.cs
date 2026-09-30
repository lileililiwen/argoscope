using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Argoscope.Domain.Signals;

namespace Argoscope.Application.Signals;

/// <summary>
/// Evidence minimization for issue/PR text. Redacts emails and likely
/// tokens, then truncates to <see cref="CommercialSignal.MaxExcerptLength"/>.
/// Only title and body enter; comments never reach this projection.
/// </summary>
public static partial class SignalTextProcessor
{
    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.Compiled)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(ghp_[A-Za-z0-9]+|gho_[A-Za-z0-9]+|github_pat_[A-Za-z0-9_]+|sk-[A-Za-z0-9\-]+|xox[baprs]-[A-Za-z0-9\-]+|Bearer\s+[A-Za-z0-9\-._~+/]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex TokenRegex();

    public static string BuildExcerpt(string title, string body)
    {
        var combined = $"{title ?? ""}\n\n{body ?? ""}".Trim();
        combined = EmailRegex().Replace(combined, "[redacted-email]");
        combined = TokenRegex().Replace(combined, "[redacted-token]");
        if (combined.Length > CommercialSignal.MaxExcerptLength)
        {
            combined = combined.Substring(0, CommercialSignal.MaxExcerptLength);
        }
        return combined;
    }

    public static bool HasUsableText(string title, string body) =>
        !string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(body);

    public static string ContentHash(string sourceType, int sourceNumber, DateTimeOffset sourceUpdatedAtUtc, string excerpt)
    {
        var raw = $"{sourceType}|{sourceNumber}|{sourceUpdatedAtUtc.ToUnixTimeSeconds()}|{excerpt}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }
}
