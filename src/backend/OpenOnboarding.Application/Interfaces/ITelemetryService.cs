namespace OpenOnboarding.Application.Interfaces;

using OpenOnboarding.Application.Contracts;

/// <summary>
/// Orchestrates dispatch of analytics events to all registered <see cref="IAnalyticsProvider"/> sinks.
/// </summary>
public interface ITelemetryService
{
    /// <summary>
    /// Dispatches <paramref name="event"/> to every registered provider in parallel.
    /// Failures from individual providers are swallowed so that the journey engine is never blocked.
    /// </summary>
    Task TrackAsync(AnalyticsEvent @event, CancellationToken cancellationToken = default);
}
