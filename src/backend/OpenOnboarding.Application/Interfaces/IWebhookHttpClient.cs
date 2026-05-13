namespace OpenOnboarding.Application.Interfaces;

public interface IWebhookHttpClient
{
    Task<WebhookHttpResponse> SendAsync(string url, string payloadJson, string signature, CancellationToken ct = default);
}

public record WebhookHttpResponse(int StatusCode, string? Body, bool IsSuccess);
