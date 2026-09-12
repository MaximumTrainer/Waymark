using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using OpenOnboarding.Application.Interfaces;

namespace OpenOnboarding.Infrastructure.Services;

/// <summary>
/// Per-session in-process delivery channels shared by every <see cref="ISessionEventEmitter"/>
/// implementation. An SSE stream is always served from the channels of the instance holding it;
/// what differs between implementations is only how an event reaches this instance.
/// <para>
/// Keeping the channel mechanics here means capacity bounds, terminal-event completion and
/// stale-channel cleanup behave identically whether the event was raised locally or arrived over
/// a distributed transport.
/// </para>
/// </summary>
public sealed class SessionEventChannels(ILogger logger)
{
    public const int ChannelCapacity = 100;

    private readonly ConcurrentDictionary<Guid, Channel<SessionEvent>> _channels = new();

    /// <summary>Event types after which no further event can arrive for the session.</summary>
    public static bool IsTerminal(string eventType)
        => eventType is "session-completed" or "session-abandoned";

    /// <summary>
    /// Delivers an event to this instance's subscriber for the session, if any.
    /// </summary>
    public void Publish(Guid sessionId, SessionEvent sessionEvent)
    {
        var channel = _channels.GetOrAdd(sessionId, _ => CreateChannel());

        if (channel.Reader.Count >= ChannelCapacity)
            logger.LogWarning("Session event channel for {SessionId} is full; oldest event dropped.", sessionId);

        // If the existing channel is already completed, replace it with a fresh one
        if (!channel.Writer.TryWrite(sessionEvent))
        {
            var fresh = CreateChannel();
            _channels[sessionId] = fresh;
            fresh.Writer.TryWrite(sessionEvent);
            channel = fresh;
        }

        if (IsTerminal(sessionEvent.EventType))
        {
            channel.Writer.TryComplete();
            // Channel stays in dict until SubscribeAsync drains it, preventing a new empty channel
            // being returned to a subscriber that arrives just after completion.
        }
    }

    public async IAsyncEnumerable<SessionEvent> SubscribeAsync(
        Guid sessionId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
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
