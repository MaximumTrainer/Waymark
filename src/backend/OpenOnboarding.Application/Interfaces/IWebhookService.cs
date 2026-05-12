using OpenOnboarding.Application.Contracts;

namespace OpenOnboarding.Application.Interfaces;

public interface IWebhookService
{
    Task<WebhookDto> RegisterAsync(Guid flowId, string url, string secret, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WebhookDto>> ListAsync(Guid flowId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid flowId, Guid webhookId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WebhookDeliveryDto>> ListDeliveriesAsync(Guid flowId, CancellationToken cancellationToken = default);
    Task DeliverAsync(Guid sessionId, Guid flowId, string eventType, object payload, CancellationToken cancellationToken = default);
}
