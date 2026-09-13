using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Application.Tests.TestHelpers;
using OpenOnboarding.Infrastructure.EventBus;
using RabbitMQ.Client;

namespace OpenOnboarding.Application.Tests;

/// <summary>
/// <see cref="SessionEventEmitterTests"/> proves the fan-out contract through an in-process fake, so
/// it covers the emitter and the port but none of the broker adapter. This is the implementation
/// that actually runs in a multi-replica deployment, and a defect in it reproduces exactly the
/// failure the distributed transport exists to prevent: an SSE stream that stays open and delivers
/// nothing.
/// <para>
/// The parts that need no broker are unit tested. The deployment-shaped case needs a real one and
/// skips with a clear reason when none is reachable.
/// </para>
/// </summary>
public sealed class RabbitMqSessionEventTransportTests
{
    private static RabbitMqSessionEventTransport CreateTransport(string exchange, string? clientProvidedName = null)
        => new(BrokerProbe.BrokerUri, exchange, NullLogger<RabbitMqSessionEventTransport>.Instance, clientProvidedName);

    // ── Wire format ────────────────────────────────────────────────────────────

    [Fact]
    public void Serialize_ThenDeserialize_RoundTripsTheWholeEnvelope()
    {
        var sessionId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 9, 13, 10, 30, 45, 123, TimeSpan.Zero);
        var original = new SessionEvent("step-completed", """{"nodeKey":"identity","attempt":2}""", timestamp);

        var envelope = RabbitMqSessionEventTransport.Deserialize(
            RabbitMqSessionEventTransport.Serialize(sessionId, original));

        Assert.NotNull(envelope);
        Assert.Equal(sessionId, envelope!.SessionId);
        Assert.Equal(original.EventType, envelope.EventType);
        Assert.Equal(original.PayloadJson, envelope.PayloadJson);
        Assert.Equal(original.Timestamp, envelope.Timestamp);

        var restored = envelope.ToSessionEvent();
        Assert.Equal(original.EventType, restored.EventType);
        Assert.Equal(original.PayloadJson, restored.PayloadJson);
        Assert.Equal(original.Timestamp, restored.Timestamp);
    }

    [Fact]
    public void Deserialize_PreservesTheTimestampOffset()
    {
        // A session trail read across regions is wrong by hours if the offset is dropped.
        var timestamp = new DateTimeOffset(2026, 9, 13, 10, 30, 45, TimeSpan.FromHours(5.5));

        var envelope = RabbitMqSessionEventTransport.Deserialize(
            RabbitMqSessionEventTransport.Serialize(Guid.NewGuid(), new SessionEvent("x", "{}", timestamp)));

        Assert.Equal(timestamp.Offset, envelope!.Timestamp.Offset);
        Assert.Equal(timestamp, envelope.Timestamp);
    }

    // ── Poison messages ────────────────────────────────────────────────────────

    [Fact]
    public async Task Deliver_MalformedBody_IsLoggedAndSwallowed()
    {
        var logger = new CapturingLogger();
        var delivered = new List<Guid>();

        await RabbitMqSessionEventTransport.DeliverAsync(
            Encoding.UTF8.GetBytes("{ this is not json"),
            (sessionId, _) => { delivered.Add(sessionId); return Task.CompletedTask; },
            logger);

        Assert.Empty(delivered);
        Assert.Contains(logger.Errors, message => message.Contains("Failed to deliver a session event"));
    }

    [Fact]
    public async Task Deliver_AfterAMalformedBody_KeepsDeliveringSubsequentMessages()
    {
        var logger = new CapturingLogger();
        var delivered = new List<string>();
        Func<Guid, SessionEvent, Task> handler = (_, evt) =>
        {
            delivered.Add(evt.EventType);
            return Task.CompletedTask;
        };

        await RabbitMqSessionEventTransport.DeliverAsync(Encoding.UTF8.GetBytes("not json at all"), handler, logger);
        await RabbitMqSessionEventTransport.DeliverAsync(
            RabbitMqSessionEventTransport.Serialize(
                Guid.NewGuid(), new SessionEvent("step-completed", "{}", DateTimeOffset.UtcNow)),
            handler,
            logger);

        Assert.Equal(["step-completed"], delivered);
    }

    [Fact]
    public async Task Deliver_WhenTheHandlerThrows_DoesNotPropagate()
    {
        // The consumer callback is the only caller; an exception escaping it kills the consumer and
        // with it every open stream on this instance.
        var logger = new CapturingLogger();

        await RabbitMqSessionEventTransport.DeliverAsync(
            RabbitMqSessionEventTransport.Serialize(
                Guid.NewGuid(), new SessionEvent("step-completed", "{}", DateTimeOffset.UtcNow)),
            (_, _) => throw new InvalidOperationException("the stream went away"),
            logger);

        Assert.Single(logger.Errors);
    }

    [Fact]
    public async Task Deliver_AJsonNullBody_IsIgnoredWithoutThrowing()
    {
        var logger = new CapturingLogger();
        var delivered = 0;

        await RabbitMqSessionEventTransport.DeliverAsync(
            Encoding.UTF8.GetBytes("null"),
            (_, _) => { delivered++; return Task.CompletedTask; },
            logger);

        Assert.Equal(0, delivered);
        Assert.Empty(logger.Errors);
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task BroadcastAsync_BeforeStartAsync_Throws()
    {
        await using var transport = CreateTransport("session-events-test");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => transport.BroadcastAsync(
                Guid.NewGuid(), new SessionEvent("step-completed", "{}", DateTimeOffset.UtcNow)));

        Assert.Contains("StartAsync", error.Message);
    }

    [Fact]
    public async Task DisposeAsync_BeforeStartAsync_DoesNotThrow()
    {
        var transport = CreateTransport("session-events-test");

        await transport.DisposeAsync();
    }

    // ── Against a real broker ──────────────────────────────────────────────────

    [RequiresBrokerFact]
    public async Task EventPublishedOnOneInstance_ReachesAnother()
    {
        var exchange = UniqueExchange();
        await using var publisher = CreateTransport(exchange);
        await using var subscriber = CreateTransport(exchange);

        var received = new ConcurrentQueue<(Guid SessionId, SessionEvent Event)>();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Both instances consume, as they do in a real deployment: the publisher receives its own
        // broadcast too, which is what makes delivery uniform across instances.
        await publisher.StartAsync((_, _) => Task.CompletedTask);
        await subscriber.StartAsync((sessionId, evt) =>
        {
            received.Enqueue((sessionId, evt));
            arrived.TrySetResult();
            return Task.CompletedTask;
        });

        var expectedSession = Guid.NewGuid();
        var expectedEvent = new SessionEvent(
            "step-completed", """{"nodeKey":"identity"}""", DateTimeOffset.UtcNow);

        await publisher.BroadcastAsync(expectedSession, expectedEvent);

        await WaitForAsync(arrived.Task, "the subscriber never received the broadcast event");

        Assert.True(received.TryDequeue(out var delivered));
        Assert.Equal(expectedSession, delivered.SessionId);
        Assert.Equal(expectedEvent.EventType, delivered.Event.EventType);
        Assert.Equal(expectedEvent.PayloadJson, delivered.Event.PayloadJson);
    }

    [RequiresBrokerFact]
    public async Task AMalformedMessageOnTheWire_DoesNotStopTheConsumer()
    {
        var exchange = UniqueExchange();
        await using var subscriber = CreateTransport(exchange);

        var good = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await subscriber.StartAsync((_, _) => { good.TrySetResult(); return Task.CompletedTask; });

        var factory = new ConnectionFactory { Uri = new Uri(BrokerProbe.BrokerUri) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(exchange, ExchangeType.Fanout, durable: true, autoDelete: false);

        await channel.BasicPublishAsync(
            exchange, routingKey: string.Empty, body: Encoding.UTF8.GetBytes("{ not an envelope"));

        await channel.BasicPublishAsync(
            exchange,
            routingKey: string.Empty,
            body: RabbitMqSessionEventTransport.Serialize(
                Guid.NewGuid(), new SessionEvent("step-completed", "{}", DateTimeOffset.UtcNow)));

        await WaitForAsync(good.Task, "the consumer stopped after a poison message");
    }

    [RequiresBrokerManagementFact]
    public async Task DeliveryResumes_AfterTheBrokerConnectionIsDropped()
    {
        var exchange = UniqueExchange();
        var subscriberName = $"session-events-test-subscriber-{Guid.NewGuid():N}";

        await using var publisher = CreateTransport(exchange);
        await using var subscriber = CreateTransport(exchange, subscriberName);

        var beforeDrop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var afterRecovery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await publisher.StartAsync((_, _) => Task.CompletedTask);
        await subscriber.StartAsync((_, evt) =>
        {
            if (evt.EventType == "before-drop") beforeDrop.TrySetResult();
            if (evt.EventType == "after-recovery") afterRecovery.TrySetResult();
            return Task.CompletedTask;
        });

        await publisher.BroadcastAsync(Guid.NewGuid(), new SessionEvent("before-drop", "{}", DateTimeOffset.UtcNow));
        await WaitForAsync(beforeDrop.Task, "the subscriber never received the pre-drop event");

        // Kill the subscriber connection from the broker side. It has to come from there: the
        // client treats an application-initiated close as deliberate and does not recover from it,
        // so closing it locally would prove nothing. Automatic recovery must now reconnect,
        // re-declare the exclusive queue and its binding, and re-attach the consumer - the topology
        // recovery this adapter configures explicitly rather than inheriting from client defaults.
        await BrokerProbe.ForceCloseConnectionAsync(subscriberName);

        // Recovery polls on NetworkRecoveryInterval, so keep republishing until it lands or we
        // give up; a single publish into the gap would be lost with nothing bound to the exchange.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (!afterRecovery.Task.IsCompleted && !cts.IsCancellationRequested)
        {
            try
            {
                await publisher.BroadcastAsync(
                    Guid.NewGuid(), new SessionEvent("after-recovery", "{}", DateTimeOffset.UtcNow));
            }
            catch (Exception)
            {
                // The publisher may be mid-recovery itself; keep trying until the deadline.
            }

            await Task.WhenAny(afterRecovery.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        }

        Assert.True(
            afterRecovery.Task.IsCompletedSuccessfully,
            "delivery did not resume within 60s of the broker connection being dropped");
    }

    private static string UniqueExchange() => $"session-events-test-{Guid.NewGuid():N}";

    private static async Task WaitForAsync(Task task, string because)
    {
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(20)));
        Assert.True(completed == task, because);
        await task;
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
                Errors.Add(formatter(state, exception));
        }
    }
}
