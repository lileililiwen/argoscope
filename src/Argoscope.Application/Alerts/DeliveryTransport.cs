using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Argoscope.Application.Alerts;

/// <summary>
/// Delivery payload and HMAC signing. Payloads carry aggregate
/// metric facts and a dashboard URL only; no issue/PR text.
/// </summary>
public static class DeliveryPayloadSigner
{
    public sealed record AlertPayload(
        string EventId,
        string RuleName,
        string PortfolioId,
        string? RepositoryId,
        string MetricKey,
        double? MetricValue,
        string MetricWindowEndUtc,
        string AsOfUtc,
        string DashboardUrl);

    public static string BuildJson(AlertPayload payload) =>
        JsonSerializer.Serialize(payload);

    public static string SignHmacSha256(string payloadJson, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadJson));
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>Result of one transport send attempt.</summary>
public sealed record DeliverySendResult(
    bool Delivered,
    bool Retryable,
    int? ResponseCode,
    string? Error);

/// <summary>
/// Transport abstraction. Production code uses HTTP/SMTP senders;
/// tests use the in-memory fake. Every implementation must
/// revalidate the destination on every send.
/// </summary>
public interface IDeliverySender
{
    Task<DeliverySendResult> SendAsync(
        Domain.Alerts.AlertChannel channel,
        string destination,
        string payloadJson,
        string? signature,
        CancellationToken cancellationToken);
}

/// <summary>
/// In-memory fake transport for deterministic tests. Queued
/// responses drive Delivered/Retryable/Permanent outcomes and
/// every call (payload + signature) is recorded for assertions.
/// </summary>
public sealed class FakeDeliverySender : IDeliverySender
{
    private readonly Queue<DeliverySendResult> _responses = new();
    public List<(string Channel, string Destination, string PayloadJson, string? Signature)> Calls { get; } = new();

    public void Enqueue(DeliverySendResult result) => _responses.Enqueue(result);

    public Task<DeliverySendResult> SendAsync(
        Domain.Alerts.AlertChannel channel,
        string destination,
        string payloadJson,
        string? signature,
        CancellationToken cancellationToken)
    {
        Calls.Add((channel.ToString(), destination, payloadJson, signature));
        if (_responses.Count > 0)
        {
            return Task.FromResult(_responses.Dequeue());
        }
        return Task.FromResult(new DeliverySendResult(true, false, 200, null));
    }
}
