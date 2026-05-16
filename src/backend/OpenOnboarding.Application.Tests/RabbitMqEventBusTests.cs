using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using OpenOnboarding.Infrastructure.EventBus;

namespace OpenOnboarding.Application.Tests;

/// <summary>
/// Unit tests for DeferredEventBus and RabbitMqEventBus retry logic
/// (GitHub issue #79: remove sync-over-async + add resilience).
/// </summary>
public sealed class RabbitMqEventBusTests
{
    // ── DeferredEventBus ───────────────────────────────────────────────────────

    [Fact]
    public async Task DeferredEventBus_ThrowsInvalidOperation_BeforeSetBus()
    {
        var bus = new DeferredEventBus();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => bus.PublishAsync(new TestNotification()));
    }

    [Fact]
    public async Task DeferredEventBus_DelegatesPublish_AfterSetBus()
    {
        var inner = new CapturingEventBus();
        var bus = new DeferredEventBus();
        bus.SetBus(inner);

        var notification = new TestNotification { Value = "hello" };
        await bus.PublishAsync(notification);

        Assert.Single(inner.Published);
        Assert.Same(notification, inner.Published[0]);
    }

    // ── RabbitMqEventBus retry logic ───────────────────────────────────────────

    [Fact]
    public async Task PublishWithRetry_SucceedsOnFirstAttempt()
    {
        var callCount = 0;
        Func<Task> send = () => { callCount++; return Task.CompletedTask; };

        await RabbitMqEventBus.PublishWithRetryAsync(
            send,
            NullLogger.Instance,
            "TestEvent",
            maxAttempts: 3,
            backoff: _ => TimeSpan.Zero,
            CancellationToken.None);

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task PublishWithRetry_RetriesAndSucceeds_OnSecondAttempt()
    {
        var callCount = 0;
        Func<Task> send = () =>
        {
            callCount++;
            if (callCount < 2) throw new InvalidOperationException("transient");
            return Task.CompletedTask;
        };

        await RabbitMqEventBus.PublishWithRetryAsync(
            send,
            NullLogger.Instance,
            "TestEvent",
            maxAttempts: 3,
            backoff: _ => TimeSpan.Zero,
            CancellationToken.None);

        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task PublishWithRetry_ThrowsAfterMaxAttempts()
    {
        var callCount = 0;
        Func<Task> send = () =>
        {
            callCount++;
            throw new InvalidOperationException("persistent failure");
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RabbitMqEventBus.PublishWithRetryAsync(
                send,
                NullLogger.Instance,
                "TestEvent",
                maxAttempts: 3,
                backoff: _ => TimeSpan.Zero,
                CancellationToken.None));

        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task PublishWithRetry_PropagatesCancellation_WithoutRetry()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var callCount = 0;
        Func<Task> send = () =>
        {
            callCount++;
            cts.Token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            RabbitMqEventBus.PublishWithRetryAsync(
                send,
                NullLogger.Instance,
                "TestEvent",
                maxAttempts: 3,
                backoff: _ => TimeSpan.Zero,
                cts.Token));

        Assert.Equal(1, callCount); // no retry on cancellation
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private record TestNotification : INotification
    {
        public string Value { get; init; } = string.Empty;
    }

    private sealed class CapturingEventBus : OpenOnboarding.Application.Interfaces.IEventBus
    {
        public List<INotification> Published { get; } = new();
        public Task PublishAsync(INotification notification, CancellationToken ct = default)
        {
            Published.Add(notification);
            return Task.CompletedTask;
        }
    }
}
