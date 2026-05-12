using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using OpenOnboarding.Application.Interfaces;

namespace OpenOnboarding.Infrastructure.Services;

public sealed class InMemorySessionEventEmitter : ISessionEventEmitter
{
    private readonly ConcurrentDictionary<Guid, Channel<SessionEvent>> _channels = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task EmitAsync(Guid sessionId, string eventType, object payload, CancellationToken cancellationToken = default)
    {
        var channel = _channels.GetOrAdd(sessionId, _ => CreateChannel());
        var evt = new SessionEvent(eventType, JsonSerializer.Serialize(payload, JsonOptions), DateTimeOffset.UtcNow);
        channel.Writer.TryWrite(evt);

        if (eventType is "session-completed" or "session-abandoned")
        {
            channel.Writer.TryComplete();
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
    }

    private static Channel<SessionEvent> CreateChannel() =>
        Channel.CreateBounded<SessionEvent>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false
        });
}
