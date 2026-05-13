using System.Text;
using OpenOnboarding.Application.Interfaces;

namespace OpenOnboarding.Infrastructure.Services;

public sealed class HttpWebhookClient(IHttpClientFactory httpClientFactory) : IWebhookHttpClient
{
    public async Task<WebhookHttpResponse> SendAsync(string url, string payloadJson, string signature, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("Webhook");
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Webhook-Signature", $"sha256={signature}");

        try
        {
            var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return new WebhookHttpResponse((int)response.StatusCode, body, response.IsSuccessStatusCode);
        }
        catch (Exception ex)
        {
            return new WebhookHttpResponse(0, ex.Message, false);
        }
    }
}
