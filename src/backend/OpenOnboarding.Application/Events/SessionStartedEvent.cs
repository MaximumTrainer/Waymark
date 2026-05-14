using MediatR;
namespace OpenOnboarding.Application.Events;
public record SessionStartedEvent(Guid SessionId, Guid FlowId, DateTimeOffset OccurredAt) : INotification;
