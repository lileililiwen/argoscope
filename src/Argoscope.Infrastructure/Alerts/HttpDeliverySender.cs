using System.Net;
using System.Text;
using Argoscope.Application.Alerts;
using Argoscope.Domain.Alerts;

namespace Argoscope.Infrastructure.Alerts;

/// <summary>
/// Production webhook transport. Revalidates the destination on
/// every send (static gate + DNS resolution + per-IP check) and
/// maps HTTP outcomes: 2xx delivered, 429/5xx retryable, other
/// 4xx permanent. Fail-closed on any safety or network error.
/// </summary>
public sealed class HttpDeliverySender : IDeliverySender
{
    private readonly HttpClient _http;

    public HttpDeliverySender(HttpClient http) => _http = http;

    public async Task<DeliverySendResult> SendAsync(
        AlertChannel channel,
        string destination,
        string payloadJson,
        string? signature,
        CancellationToken cancellationToken)
    {
        if (channel != AlertChannel.Webhook)
        {
            return new DeliverySendResult(false, false, null, "email-not-configured");
        }
        var gate = await AlertService.WebhookGateAsync(destination, cancellationToken).ConfigureAwait(false);
        if (!gate.IsAllowed)
        {
            return new DeliverySendResult(false, false, null, gate.Reason ?? "webhook-destination-rejected");
        }
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, destination)
            {
                Content = new StringContent(payloadJson, Encoding.UTF8, "application/json"),
            };
            if (!string.IsNullOrEmpty(signature))
            {
                request.Headers.Add("X-Argoscope-Signature", signature);
            }
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await _http.SendAsync(request, cts.Token).ConfigureAwait(false);
            var code = (int)response.StatusCode;
            if (code is >= 200 and < 300)
            {
                return new DeliverySendResult(true, false, code, null);
            }
            if (code == 429 || code >= 500)
            {
                return new DeliverySendResult(false, true, code, $"webhook-retryable-{code}");
            }
            return new DeliverySendResult(false, false, code, $"webhook-permanent-{code}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return new DeliverySendResult(false, true, null, $"webhook-transport-{ex.GetType().Name}");
        }
    }
}
