using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using OpenOnboarding.Application.Interfaces;

namespace OpenOnboarding.Infrastructure.Services;

public sealed class InMemorySessionEventEmitter : ISessionEventEmitter
{
    private const int ChannelCapacity = 100;
    private readonly ConcurrentDictionary<Guid, Channel<SessionEvent>> _channels = new();
    private readonly ILogger<InMemorySessionEventEmitter> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public InMemorySessionEventEmitter(ILogger<InMemorySessionEventEmitter> logger)
    {
        _logger = logger;
    }

    public Task EmitAsync(Guid sessionId, string eventType, object payload, CancellationToken cancellationToken = default)
    {
        var channel = _channels.GetOrAdd(sessionId, _ => CreateChannel());
        var evt = new SessionEvent(eventType, JsonSerializer.Serialize(payload, JsonOptions), DateTimeOffset.UtcNow);

        if (channel.Reader.Count >= ChannelCapacity)
            _logger.LogWarning("Session event channel for {SessionId} is full; oldest event dropped.", sessionId);

        // If the existing channel is already completed, replace it with a fresh one
        if (!channel.Writer.TryWrite(evt))
        {
            var fresh = CreateChannel();
            _channels[sessionId] = fresh;
            fresh.Writer.TryWrite(evt);
            channel = fresh;
        }

        if (eventType is "session-completed" or "session-abandoned")
        {
            channel.Writer.TryComplete();
            // Channel stays in dict until SubscribeAsync drains it, preventing a new empty channel
            // being returned to a subscriber that arrives just after completion.
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<SessionEvent> SubscribeAsync(Guid sessionId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = _channels.GetOrAdd(sessionId, _ => CreateChannel());

        await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return evt;
        }

        // Remove the entry after the subscriber has drained it so no stale channels accumulate.
        _channels.TryRemove(sessionId, out _);
    }

    private static Channel<SessionEvent> CreateChannel() =>
        Channel.CreateBounded<SessionEvent>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false
        });
}
