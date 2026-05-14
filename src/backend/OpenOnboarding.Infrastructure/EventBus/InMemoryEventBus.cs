using MediatR;
using OpenOnboarding.Application.Interfaces;

namespace OpenOnboarding.Infrastructure.EventBus;

/// <summary>In-process event bus: publishes to all in-process MediatR notification handlers (projectors).</summary>
internal sealed class InMemoryEventBus(IPublisher publisher) : IEventBus
{
    public Task PublishAsync(INotification notification, CancellationToken cancellationToken = default)
        => publisher.Publish(notification, cancellationToken);
}
