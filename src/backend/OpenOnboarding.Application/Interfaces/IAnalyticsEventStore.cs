using OpenOnboarding.Application.Contracts;

namespace OpenOnboarding.Application.Interfaces;

/// <summary>
/// Reads back and prunes the durable analytics event trail.
/// <para>
/// Writing goes through <see cref="ITelemetryService"/> so every sink sees the event; this port is
/// only for the queries and the retention sweep, which are specific to the durable store.
/// </para>
/// </summary>
public interface IAnalyticsEventStore
{
    /// <summary>Returns one session's events in the order they occurred.</summary>
    Task<IReadOnlyList<AnalyticsEvent>> GetSessionTrailAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes events recorded before <paramref name="threshold"/> and returns how many were
    /// removed. Measured on write time, which a client cannot backdate.
    /// </summary>
    Task<int> DeleteRecordedBeforeAsync(
        DateTimeOffset threshold,
        CancellationToken cancellationToken = default);
}
