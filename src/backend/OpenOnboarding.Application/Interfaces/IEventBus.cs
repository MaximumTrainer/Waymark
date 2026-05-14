using MediatR;
namespace OpenOnboarding.Application.Interfaces;
/// <summary>Out-of-process event bus port. Implementations may publish to RabbitMQ, Kafka, etc.</summary>
public interface IEventBus
{
    Task PublishAsync(INotification notification, CancellationToken cancellationToken = default);
}
