using MediatR;
using OpenOnboarding.Application.Interfaces;

namespace OpenOnboarding.Infrastructure.EventBus;

/// <summary>
/// Placeholder IEventBus that delegates to the real RabbitMQ bus once it has
/// been asynchronously initialised by RabbitMqEventBusInitializer. Registered
/// as the singleton IEventBus so the DI graph can be constructed synchronously.
/// </summary>
public sealed class DeferredEventBus : IEventBus
{
    private volatile IEventBus? _inner;

    public void SetBus(IEventBus bus) => _inner = bus;

    public Task PublishAsync(INotification notification, CancellationToken cancellationToken = default)
    {
        if (_inner is null)
            throw new InvalidOperationException(
                "RabbitMQ event bus has not been initialised yet. " +
                "Ensure RabbitMqEventBusInitializer has started before publishing events.");

        return _inner.PublishAsync(notification, cancellationToken);
    }
}
