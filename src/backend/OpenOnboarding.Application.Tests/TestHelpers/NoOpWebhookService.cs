using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Interfaces;

namespace OpenOnboarding.Application.Tests.TestHelpers;

internal sealed class NoOpWebhookService : IWebhookService
{
    public Task<WebhookDto> RegisterAsync(Guid flowId, string url, string secret, CancellationToken cancellationToken = default)
        => Task.FromResult(new WebhookDto(Guid.NewGuid(), flowId, url, DateTimeOffset.UtcNow));

    public Task<IReadOnlyList<WebhookDto>> ListAsync(Guid flowId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<WebhookDto>>([]);

    public Task DeleteAsync(Guid flowId, Guid webhookId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<WebhookDeliveryDto>> ListDeliveriesAsync(Guid flowId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<WebhookDeliveryDto>>([]);

    public Task DeliverAsync(Guid sessionId, Guid flowId, string eventType, object payload, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
