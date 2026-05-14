using MediatR;
namespace OpenOnboarding.Application.Events;
public record SessionAbandonedEvent(Guid SessionId, Guid FlowId, DateTimeOffset OccurredAt) : INotification;
