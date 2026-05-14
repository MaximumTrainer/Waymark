using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Infrastructure.Services;

namespace OpenOnboarding.Application.Tests;

public sealed class TelemetryServiceTests
{
    [Fact]
    public async Task TrackAsync_CallsAllRegisteredProviders()
    {
        var providerA = new SpyAnalyticsProvider();
        var providerB = new SpyAnalyticsProvider();
        var service = new TelemetryService([providerA, providerB], NullLogger<TelemetryService>.Instance);

        var @event = BuildEvent("session_started");
        await service.TrackAsync(@event);

        Assert.Single(providerA.ReceivedEvents);
        Assert.Single(providerB.ReceivedEvents);
        Assert.Equal("session_started", providerA.ReceivedEvents[0].EventType);
        Assert.Equal("session_started", providerB.ReceivedEvents[0].EventType);
    }

    [Fact]
    public async Task TrackAsync_DoesNotThrow_WhenProviderFails()
    {
        var faultyProvider = new FaultyAnalyticsProvider();
        var service = new TelemetryService([faultyProvider], NullLogger<TelemetryService>.Instance);

        var @event = BuildEvent("journey_complete");

        // Should not throw even though the provider throws
        var exception = await Record.ExceptionAsync(() => service.TrackAsync(@event));
        Assert.Null(exception);
    }

    [Fact]
    public async Task TrackAsync_HealthyProviderStillInvoked_WhenOtherProviderFails()
    {
        var faultyProvider = new FaultyAnalyticsProvider();
        var healthyProvider = new SpyAnalyticsProvider();
        var service = new TelemetryService([faultyProvider, healthyProvider], NullLogger<TelemetryService>.Instance);

        var @event = BuildEvent("navigation_next");
        await service.TrackAsync(@event);

        Assert.Single(healthyProvider.ReceivedEvents);
    }

    [Fact]
    public async Task TrackAsync_DoesNothing_WhenNoProvidersRegistered()
    {
        var service = new TelemetryService([], NullLogger<TelemetryService>.Instance);
        var @event = BuildEvent("step_viewed");

        // Should complete without error
        var exception = await Record.ExceptionAsync(() => service.TrackAsync(@event));
        Assert.Null(exception);
    }

    [Fact]
    public async Task TrackAsync_PassesEventDataUnchanged_ToProvider()
    {
        var provider = new SpyAnalyticsProvider();
        var service = new TelemetryService([provider], NullLogger<TelemetryService>.Instance);

        var @event = new AnalyticsEvent
        {
            EventType = "journey_complete",
            JourneyId = "journey-123",
            SessionId = "session-456",
            StepId = "step-789",
            StepIndex = 3,
            Payload = new Dictionary<string, object?> { ["key"] = "value" }
        };
        await service.TrackAsync(@event);

        var received = Assert.Single(provider.ReceivedEvents);
        Assert.Equal("journey_complete", received.EventType);
        Assert.Equal("journey-123", received.JourneyId);
        Assert.Equal("session-456", received.SessionId);
        Assert.Equal("step-789", received.StepId);
        Assert.Equal(3, received.StepIndex);
        Assert.Equal("value", received.Payload["key"]);
    }

    [Fact]
    public async Task TrackAsync_DoesNotLogWarning_WhenProviderIsCancelledByRequestToken()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        var logger = new SpyLogger<TelemetryService>();
        var provider = new CancelledAnalyticsProvider();
        var service = new TelemetryService([provider], logger);

        var @event = BuildEvent("navigation_next");
        await service.TrackAsync(@event, cancellationTokenSource.Token);

        Assert.Equal(0, logger.WarningCount);
    }

    private static AnalyticsEvent BuildEvent(string eventType) => new()
    {
        EventType = eventType,
        JourneyId = Guid.NewGuid().ToString(),
        SessionId = Guid.NewGuid().ToString()
    };

    private sealed class SpyAnalyticsProvider : IAnalyticsProvider
    {
        public List<AnalyticsEvent> ReceivedEvents { get; } = [];

        public Task TrackEventAsync(AnalyticsEvent @event, CancellationToken cancellationToken = default)
        {
            ReceivedEvents.Add(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class FaultyAnalyticsProvider : IAnalyticsProvider
    {
        public Task TrackEventAsync(AnalyticsEvent @event, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated provider failure");
        }
    }

    private sealed class CancelledAnalyticsProvider : IAnalyticsProvider
    {
        public Task TrackEventAsync(AnalyticsEvent @event, CancellationToken cancellationToken = default)
        {
            return Task.FromCanceled(cancellationToken);
        }
    }

    private sealed class SpyLogger<T> : ILogger<T>
    {
        public int WarningCount { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                WarningCount++;
            }
        }
    }
}
