using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenOnboarding.Application.Interfaces;

namespace OpenOnboarding.Infrastructure.Services;

/// <summary>
/// Session event emitter for multi-replica deployments. Every event is broadcast over an
/// <see cref="ISessionEventTransport"/> and delivered to the local SSE channels of every instance,
/// so a stream held by one replica still receives events raised on another.
/// <para>
/// Registered as an <see cref="IHostedService"/> so the transport subscription is established
/// during startup, before the first request is served.
/// </para>
/// </summary>
public sealed class DistributedSessionEventEmitter : ISessionEventEmitter, IHostedService, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISessionEventTransport _transport;
    private readonly ILogger<DistributedSessionEventEmitter> _logger;
    private readonly SessionEventChannels _channels;

    public DistributedSessionEventEmitter(
        ISessionEventTransport transport,
        ILogger<DistributedSessionEventEmitter> logger)
    {
        _transport = transport;
        _logger = logger;
        _channels = new SessionEventChannels(logger);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _transport.StartAsync(DeliverLocallyAsync, cancellationToken);
        _logger.LogInformation(
            "Distributed session event transport started; SSE streams will receive events raised on any instance.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task EmitAsync(
        Guid sessionId,
        string eventType,
        object payload,
        CancellationToken cancellationToken = default)
    {
        var evt = new SessionEvent(eventType, JsonSerializer.Serialize(payload, JsonOptions), DateTimeOffset.UtcNow);

        // Broadcast only. The transport echoes back to this instance, so publishing locally here
        // as well would deliver the event twice to a stream held by the emitting instance.
        await _transport.BroadcastAsync(sessionId, evt, cancellationToken);
    }

    public IAsyncEnumerable<SessionEvent> SubscribeAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => _channels.SubscribeAsync(sessionId, cancellationToken);

    private Task DeliverLocallyAsync(Guid sessionId, SessionEvent sessionEvent)
    {
        _channels.Publish(sessionId, sessionEvent);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => _transport.DisposeAsync();
}
