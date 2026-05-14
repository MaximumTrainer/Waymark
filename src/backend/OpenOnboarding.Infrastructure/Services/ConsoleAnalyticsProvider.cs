using Microsoft.Extensions.Logging;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Interfaces;

namespace OpenOnboarding.Infrastructure.Services;

/// <summary>
/// Development / debug analytics provider that writes events to the application logger.
/// Enabled by default; configure <c>Analytics:ConsoleProvider:Enabled</c> to <c>false</c>
/// in production to suppress the output without unregistering the provider.
/// </summary>
public sealed class ConsoleAnalyticsProvider(ILogger<ConsoleAnalyticsProvider> logger) : IAnalyticsProvider
{
    public Task TrackEventAsync(AnalyticsEvent @event, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "[Analytics] EventType={EventType} EventId={EventId} JourneyId={JourneyId} " +
            "SessionId={SessionId} StepId={StepId} StepIndex={StepIndex} OccurredAt={OccurredAt} Payload={@Payload}",
            @event.EventType,
            @event.EventId,
            @event.JourneyId,
            @event.SessionId,
            @event.StepId,
            @event.StepIndex,
            @event.OccurredAt,
            @event.Payload);

        return Task.CompletedTask;
    }
}
