using System.Security.Cryptography;
using System.Text;

namespace Argoscope.Application.Billing;

/// <summary>
/// Provider-neutral HMAC-SHA256 webhook signatures (contract
/// "billing-webhook-v1", Stripe-compatible "t,v1" scheme). The signature is
/// computed over "{timestamp}.{rawBody}" so deliveries are bound to their
/// send time and replays outside the tolerance window are rejected.
/// </summary>
public static class WebhookSigner
{
    public static string ComputeSignature(string secret, string timestamp, string rawBody)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{rawBody}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Parse a "t={unix},v1={hex}" header (bare hex is also accepted
    /// for provider variants that send the timestamp in the envelope).</summary>
    public static bool TryParseHeader(string? header, out string timestamp, out string signature)
    {
        timestamp = string.Empty;
        signature = string.Empty;
        if (string.IsNullOrWhiteSpace(header)) return false;

        var parts = header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (part.StartsWith("t=", StringComparison.Ordinal))
            {
                timestamp = part[2..];
            }
            else if (part.StartsWith("v1=", StringComparison.Ordinal))
            {
                signature = part[3..];
            }
        }

        if (string.IsNullOrEmpty(signature) && parts.Length == 1 && !header.Contains('='))
        {
            signature = header.Trim();
        }

        return !string.IsNullOrEmpty(signature);
    }

    public static bool Verify(string secret, string timestamp, string rawBody, string signature)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(signature)) return false;
        var expected = ComputeSignature(secret, timestamp, rawBody);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant()));
    }
}
