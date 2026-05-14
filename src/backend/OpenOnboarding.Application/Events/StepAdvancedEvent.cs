using MediatR;
namespace OpenOnboarding.Application.Events;
public record StepAdvancedEvent(Guid SessionId, Guid FlowId, Guid? CurrentNodeId, string? CurrentNodeKey, DateTimeOffset OccurredAt) : INotification;
