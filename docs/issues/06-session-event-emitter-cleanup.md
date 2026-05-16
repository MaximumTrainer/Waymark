---
title: "InMemorySessionEventEmitter — document channel eviction behaviour and add stale-channel cleanup"
labels: ["reliability", "observability"]
---

## Summary

`InMemorySessionEventEmitter` uses a `ConcurrentDictionary<Guid, Channel<SessionEvent>>` to fan events to SSE subscribers. Channels are created on first access and completed (closed) when a `session-completed` or `session-abandoned` event is emitted. However:

1. **Stale channels are never removed from the dictionary.** A completed channel's entry stays in `_channels` indefinitely. In a long-running process with many sessions this is a slow memory leak.
2. **The channel is bounded to 100 items with `DropOldest`.** If a subscriber is slow, events are silently dropped. There is no log or metric when this occurs.
3. **There is no test** that verifies channel completion, event ordering, or the DropOldest eviction behaviour.

**Affected file:** `src/backend/OpenOnboarding.Infrastructure/Services/InMemorySessionEventEmitter.cs`

### Current implementation

```csharp
public sealed class InMemorySessionEventEmitter : ISessionEventEmitter
{
    private readonly ConcurrentDictionary<Guid, Channel<SessionEvent>> _channels = new();

    public Task EmitAsync(Guid sessionId, string eventType, object payload, ...)
    {
        var channel = _channels.GetOrAdd(sessionId, _ => CreateChannel());
        // ...
        if (eventType is "session-completed" or "session-abandoned")
            channel.Writer.TryComplete();      // ← channel closed but entry never removed

        return Task.CompletedTask;
    }

    private static Channel<SessionEvent> CreateChannel() =>
        Channel.CreateBounded<SessionEvent>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest,   // ← silent drop
            ...
        });
}
```

---

## Requirements

### Fix 1 — Remove completed channel entries

After calling `TryComplete()`, remove the entry from `_channels`:

```csharp
channel.Writer.TryComplete();
_channels.TryRemove(sessionId, out _);
```

This bounds dictionary growth to the number of active (open) sessions rather than all sessions ever seen.

### Fix 2 — Log event drops

Override the `DropOldest` path by implementing a custom `IChannelWriter` wrapper, or use a `Channel.CreateUnbounded` with a configurable high-watermark soft limit and log a warning when the count exceeds it.

At minimum, add a single log warning at `Warning` level:

```
Session event channel for {SessionId} is full; oldest event dropped.
```

This requires a different channel strategy since `System.Threading.Channels` does not natively callback on drop. Consider switching to `BoundedChannelFullMode.Wait` (to apply backpressure) or `DropOldest` with a logged wrapper.

### Fix 3 — Unit tests

Add `InMemorySessionEventEmitterTests` in `OpenOnboarding.Application.Tests`:

| # | Scenario | Expected outcome |
|---|----------|-----------------|
| 1 | Emit and subscribe — events are received in order | All emitted events returned by `SubscribeAsync` |
| 2 | Emit `session-completed` — channel is completed | `SubscribeAsync` async enumerable completes without `CancellationToken` |
| 3 | Emit `session-abandoned` — channel is completed | Same as above |
| 4 | After `session-completed`, entry is removed from internal dictionary | A second call to `EmitAsync` for the same session ID creates a fresh channel |
| 5 | Subscribing before any events — events emitted later are still received | Ordering guarantee |

---

## Acceptance Criteria

- [ ] `_channels` dictionary no longer retains entries for sessions whose channel has been completed.
- [ ] Event drops (when channel is full) produce a `LogLevel.Warning` log entry containing the session ID.
- [ ] The 5 unit test cases above exist and are green.
- [ ] Existing `DocumentUploadAndSseTests` SSE tests pass without modification.
- [ ] `dotnet test` passes.
