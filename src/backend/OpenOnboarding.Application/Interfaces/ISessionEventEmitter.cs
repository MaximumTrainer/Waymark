namespace OpenOnboarding.Application.Interfaces;

public record SessionEvent(string EventType, string PayloadJson, DateTimeOffset Timestamp);

public interface ISessionEventEmitter
{
    Task EmitAsync(Guid sessionId, string eventType, object payload, CancellationToken cancellationToken = default);
    IAsyncEnumerable<SessionEvent> SubscribeAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
