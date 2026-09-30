using System.Net;
using System.Net.Sockets;

namespace Argoscope.Domain.Alerts;

/// <summary>
/// Static (no-DNS) webhook safety gate used at rule-configuration time.
/// Full DNS resolution + per-IP validation happens on every send.
/// Rejects non-HTTPS, loopback, private, link-local, multicast and
/// reserved targets. Fail-closed: unparseable or unresolvable
/// literals are rejected.
/// </summary>
public static class WebhookSafety
{
    public sealed record CheckResult(bool IsAllowed, string? Reason);

    public static CheckResult CheckStatic(string destination)
    {
        if (!Uri.TryCreate(destination, UriKind.Absolute, out var uri))
        {
            return new CheckResult(false, "Webhook destination must be an absolute HTTPS URL.");
        }
        if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            return new CheckResult(false, "Webhook destination must use HTTPS.");
        }
        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            return new CheckResult(false, "Webhook destination must have a host.");
        }
        if (uri.Port != 443 && (uri.IsDefaultPort == false && uri.Port is < 1 or > 65535))
        {
            return new CheckResult(false, "Webhook destination port is invalid.");
        }

        // Literal IPs can be judged without DNS.
        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            return CheckIp(literal);
        }

        var host = uri.Host.Trim().ToLowerInvariant();
        if (host is "localhost" || host.EndsWith(".localhost", StringComparison.Ordinal))
        {
            return new CheckResult(false, "Webhook destination must not target localhost.");
        }
        if (host.EndsWith(".local", StringComparison.Ordinal)
            || host.EndsWith(".internal", StringComparison.Ordinal)
            || host.EndsWith(".lan", StringComparison.Ordinal))
        {
            return new CheckResult(false, "Webhook destination must not target private zones.");
        }
        return new CheckResult(true, null);
    }

    public static CheckResult CheckIp(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (IPAddress.IsLoopback(address))
        {
            return new CheckResult(false, "Webhook destination must not target loopback addresses.");
        }
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            // 10/8, 172.16/12, 192.168/16, 127/8 (covered), 169.254/16, 0/8,
            // multicast 224/4, reserved 240/4, broadcast.
            if (bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254)
                || bytes[0] == 0
                || bytes[0] >= 224)
            {
                return new CheckResult(false, "Webhook destination must not target private/link-local/reserved addresses.");
            }
            return new CheckResult(true, null);
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal)
            {
                return new CheckResult(false, "Webhook destination must not target link-local/multicast/site-local addresses.");
            }
            // ::1 covered by IsLoopback; unique-local fc00::/7.
            if ((bytes[0] & 0xfe) == 0xfc)
            {
                return new CheckResult(false, "Webhook destination must not target private IPv6 addresses.");
            }
            return new CheckResult(true, null);
        }
        return new CheckResult(false, "Webhook destination address family is not supported.");
    }
}
