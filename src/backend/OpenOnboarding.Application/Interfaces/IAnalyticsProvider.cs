namespace OpenOnboarding.Application.Interfaces;

using OpenOnboarding.Application.Contracts;

/// <summary>
/// Pluggable analytics destination. Implement this interface to send journey events
/// to a third-party provider (e.g. PostHog, Segment, Mixpanel).
/// </summary>
public interface IAnalyticsProvider
{
    /// <summary>
    /// Tracks a single analytics event.  The <see cref="ITelemetryService"/> orchestrator
    /// catches any exceptions thrown by implementations and logs them as warnings, so
    /// a faulting provider never blocks the journey engine.  Implementations should
    /// still prefer to handle their own errors internally for maximum observability.
    /// </summary>
    Task TrackEventAsync(AnalyticsEvent @event, CancellationToken cancellationToken = default);
}
