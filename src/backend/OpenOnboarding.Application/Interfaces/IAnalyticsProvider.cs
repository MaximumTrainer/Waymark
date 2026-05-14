namespace OpenOnboarding.Application.Interfaces;

using OpenOnboarding.Application.Contracts;

/// <summary>
/// Pluggable analytics destination. Implement this interface to send journey events
/// to a third-party provider (e.g. PostHog, Segment, Mixpanel).
/// </summary>
public interface IAnalyticsProvider
{
    /// <summary>
    /// Tracks a single analytics event.  Implementations must not throw —
    /// any failures should be swallowed and logged internally.
    /// </summary>
    Task TrackEventAsync(AnalyticsEvent @event, CancellationToken cancellationToken = default);
}
