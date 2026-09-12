using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenOnboarding.Application.Interfaces;

namespace OpenOnboarding.Infrastructure.Services;

/// <summary>
/// Single-instance session event emitter. Events are delivered only to SSE streams held by this
/// process, so it is correct for local development, tests, and a deployment pinned to one replica.
/// Scale beyond one replica requires <see cref="DistributedSessionEventEmitter"/>; see
/// <c>SessionEvents:Transport</c> in the runbook.
/// </summary>
public sealed class InMemorySessionEventEmitter : ISessionEventEmitter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SessionEventChannels _channels;

    public InMemorySessionEventEmitter(ILogger<InMemorySessionEventEmitter> logger)
    {
        _channels = new SessionEventChannels(logger);
    }

    public Task EmitAsync(Guid sessionId, string eventType, object payload, CancellationToken cancellationToken = default)
    {
        var evt = new SessionEvent(eventType, JsonSerializer.Serialize(payload, JsonOptions), DateTimeOffset.UtcNow);
        _channels.Publish(sessionId, evt);
        return Task.CompletedTask;
    }

    public IAsyncEnumerable<SessionEvent> SubscribeAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => _channels.SubscribeAsync(sessionId, cancellationToken);
}
