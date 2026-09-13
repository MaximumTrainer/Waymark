namespace OpenOnboarding.Application.Interfaces;

/// <summary>
/// Port for a transport that fans a session event out to every API instance.
/// <para>
/// Session progress is delivered to browsers over SSE, and an SSE stream is pinned to the single
/// instance that accepted it. An event raised while handling a request on another instance must
/// therefore travel between instances to reach that stream. This port is that hop; adapters
/// implement it over a broadcast-capable broker (RabbitMQ fanout, Redis pub/sub, ...).
/// </para>
/// </summary>
public interface ISessionEventTransport : IAsyncDisposable
{
    /// <summary>
    /// Begins delivering broadcast events to <paramref name="handler"/>. Called once during
    /// startup. The handler is invoked for events published by every instance, the caller's
    /// own included, so a broadcast is delivered exactly once everywhere.
    /// </summary>
    Task StartAsync(Func<Guid, SessionEvent, Task> handler, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes an event to every instance subscribed to the transport.
    /// </summary>
    Task BroadcastAsync(Guid sessionId, SessionEvent sessionEvent, CancellationToken cancellationToken = default);
}
