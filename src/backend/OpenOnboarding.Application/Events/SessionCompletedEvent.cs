using MediatR;
namespace OpenOnboarding.Application.Events;
public record SessionCompletedEvent(Guid SessionId, Guid FlowId, DateTimeOffset OccurredAt) : INotification;
