using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Infrastructure.Services;

namespace OpenOnboarding.Application.Tests;

/// <summary>
/// Behaviour shared by every <see cref="ISessionEventEmitter"/> implementation, plus the fan-out
/// contract that only the distributed emitter satisfies.
/// </summary>
public sealed class SessionEventEmitterTests
{
    // -----------------------------------------------------------------------
    // In-process transport: stands in for a broker so two "instances" can be
    // wired together without one. Fans every broadcast out to all subscribers,
    // the publisher included, exactly as a RabbitMQ fanout exchange does.
    // -----------------------------------------------------------------------
    private sealed class InProcessSessionEventTransport : ISessionEventTransport
    {
        private readonly ConcurrentBag<Func<Guid, SessionEvent, Task>> _handlers = [];

        public Task StartAsync(Func<Guid, SessionEvent, Task> handler, CancellationToken cancellationToken = default)
        {
            _handlers.Add(handler);
            return Task.CompletedTask;
        }

        public async Task BroadcastAsync(Guid sessionId, SessionEvent sessionEvent, CancellationToken cancellationToken = default)
        {
            foreach (var handler in _handlers)
                await handler(sessionId, sessionEvent);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static DistributedSessionEventEmitter CreateDistributed(ISessionEventTransport transport)
        => new(transport, NullLogger<DistributedSessionEventEmitter>.Instance);

    private static InMemorySessionEventEmitter CreateInMemory()
        => new(NullLogger<InMemorySessionEventEmitter>.Instance);

    /// <summary>Reads up to <paramref name="count"/> events, giving up after a timeout.</summary>
    private static async Task<List<SessionEvent>> ReadAsync(
        ISessionEventEmitter emitter, Guid sessionId, int count, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        var collected = new List<SessionEvent>();

        try
        {
            await foreach (var evt in emitter.SubscribeAsync(sessionId, cts.Token))
            {
                collected.Add(evt);
                if (collected.Count >= count)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Timed out; return what arrived so the assertion reports the real shortfall.
        }

        return collected;
    }

    // =======================================================================
    // Fan-out: the defect this issue is about
    // =======================================================================
    [Fact]
    public async Task DistributedEmitter_EventRaisedOnInstanceA_ReachesStreamOnInstanceB()
    {
        await using var transport = new InProcessSessionEventTransport();

        // Two API instances sharing one transport.
        await using var instanceA = CreateDistributed(transport);
        await using var instanceB = CreateDistributed(transport);
        await instanceA.StartAsync(CancellationToken.None);
        await instanceB.StartAsync(CancellationToken.None);

        var sessionId = Guid.NewGuid();

        // The applicant's SSE stream is held by instance B.
        var streamOnB = ReadAsync(instanceB, sessionId, count: 1);

        // Their step submission is handled by instance A.
        await instanceA.EmitAsync(sessionId, "step-advanced", new { nodeKey = "address" });

        var received = await streamOnB;

        var evt = Assert.Single(received);
        Assert.Equal("step-advanced", evt.EventType);
        Assert.Contains("address", evt.PayloadJson);
    }

    [Fact]
    public async Task InMemoryEmitter_EventRaisedOnInstanceA_DoesNotReachInstanceB()
    {
        // Documents the limitation the distributed emitter exists to remove: two in-memory
        // emitters are two isolated processes.
        var instanceA = CreateInMemory();
        var instanceB = CreateInMemory();
        var sessionId = Guid.NewGuid();

        var streamOnB = ReadAsync(instanceB, sessionId, count: 1, timeout: TimeSpan.FromMilliseconds(300));
        await instanceA.EmitAsync(sessionId, "step-advanced", new { nodeKey = "address" });

        Assert.Empty(await streamOnB);
    }

    [Fact]
    public async Task DistributedEmitter_DoesNotDeliverTwiceToTheEmittingInstance()
    {
        await using var transport = new InProcessSessionEventTransport();
        await using var instance = CreateDistributed(transport);
        await instance.StartAsync(CancellationToken.None);

        var sessionId = Guid.NewGuid();
        var stream = ReadAsync(instance, sessionId, count: 2, timeout: TimeSpan.FromMilliseconds(500));

        await instance.EmitAsync(sessionId, "step-advanced", new { step = 1 });

        // Only one event was emitted; a second arrival would mean the transport echo was
        // published locally as well.
        var received = await stream;
        Assert.Single(received);
    }

    // =======================================================================
    // Terminal events close the stream on every instance
    // =======================================================================
    [Theory]
    [InlineData("session-completed")]
    [InlineData("session-abandoned")]
    public async Task DistributedEmitter_TerminalEvent_CompletesStreamOnEveryInstance(string terminalEvent)
    {
        await using var transport = new InProcessSessionEventTransport();
        await using var instanceA = CreateDistributed(transport);
        await using var instanceB = CreateDistributed(transport);
        await instanceA.StartAsync(CancellationToken.None);
        await instanceB.StartAsync(CancellationToken.None);

        var sessionId = Guid.NewGuid();

        // Ask for more events than will ever arrive: the stream must end on its own when the
        // terminal event completes the channel, rather than running to the timeout.
        var streamOnB = ReadAsync(instanceB, sessionId, count: 99, timeout: TimeSpan.FromSeconds(5));

        await instanceA.EmitAsync(sessionId, terminalEvent, new { reason = "done" });

        var received = await streamOnB;
        var evt = Assert.Single(received);
        Assert.Equal(terminalEvent, evt.EventType);
    }

    [Theory]
    [InlineData("session-completed")]
    [InlineData("session-abandoned")]
    public async Task InMemoryEmitter_TerminalEvent_CompletesStream(string terminalEvent)
    {
        var emitter = CreateInMemory();
        var sessionId = Guid.NewGuid();

        var stream = ReadAsync(emitter, sessionId, count: 99, timeout: TimeSpan.FromSeconds(5));
        await emitter.EmitAsync(sessionId, terminalEvent, new { reason = "done" });

        var evt = Assert.Single(await stream);
        Assert.Equal(terminalEvent, evt.EventType);
    }

    // =======================================================================
    // Capacity and cleanup behave identically for both implementations
    // =======================================================================
    public static TheoryData<string> EmitterKinds => new() { "in-memory", "distributed" };

    private static async Task<ISessionEventEmitter> CreateAsync(string kind, ISessionEventTransport transport)
    {
        if (kind == "in-memory")
            return CreateInMemory();

        var emitter = CreateDistributed(transport);
        await emitter.StartAsync(CancellationToken.None);
        return emitter;
    }

    [Theory]
    [MemberData(nameof(EmitterKinds))]
    public async Task Emitter_BoundedChannel_KeepsMostRecentEventsWhenNoSubscriberDrains(string kind)
    {
        await using var transport = new InProcessSessionEventTransport();
        var emitter = await CreateAsync(kind, transport);
        var sessionId = Guid.NewGuid();

        // Overfill the bounded channel with no subscriber attached.
        var overflow = SessionEventChannels.ChannelCapacity + 20;
        for (var i = 0; i < overflow; i++)
            await emitter.EmitAsync(sessionId, "step-advanced", new { index = i });

        var received = await ReadAsync(
            emitter, sessionId, SessionEventChannels.ChannelCapacity, TimeSpan.FromSeconds(5));

        // DropOldest: capacity retained, and the newest event survived.
        Assert.Equal(SessionEventChannels.ChannelCapacity, received.Count);
        Assert.Contains($"{overflow - 1}", received[^1].PayloadJson);
        Assert.DoesNotContain(received, e => e.PayloadJson.Contains("\"index\":0}"));
    }

    [Theory]
    [MemberData(nameof(EmitterKinds))]
    public async Task Emitter_DrainedSubscriber_LeavesNoStaleChannel(string kind)
    {
        await using var transport = new InProcessSessionEventTransport();
        var emitter = await CreateAsync(kind, transport);
        var sessionId = Guid.NewGuid();

        var stream = ReadAsync(emitter, sessionId, count: 99, timeout: TimeSpan.FromSeconds(5));
        await emitter.EmitAsync(sessionId, "session-completed", new { });
        await stream;

        // The channel was removed on drain, so a fresh subscriber gets a live channel rather than
        // the completed one - it receives a subsequent event instead of ending immediately.
        var secondStream = ReadAsync(emitter, sessionId, count: 1, timeout: TimeSpan.FromSeconds(5));
        await emitter.EmitAsync(sessionId, "step-advanced", new { step = "restarted" });

        var evt = Assert.Single(await secondStream);
        Assert.Equal("step-advanced", evt.EventType);
    }
}
