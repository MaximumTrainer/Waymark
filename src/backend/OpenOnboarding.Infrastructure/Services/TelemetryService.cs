using Microsoft.Extensions.Logging;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Interfaces;

namespace OpenOnboarding.Infrastructure.Services;

/// <summary>
/// Orchestrates dispatch of <see cref="AnalyticsEvent"/> instances to all registered
/// <see cref="IAnalyticsProvider"/> sinks in parallel.  Individual provider failures are
/// swallowed so that the journey engine is never disrupted by a bad analytics integration.
/// </summary>
public sealed class TelemetryService(
    IEnumerable<IAnalyticsProvider> providers,
    ILogger<TelemetryService> logger) : ITelemetryService
{
    private readonly IReadOnlyList<IAnalyticsProvider> _providers = providers.ToList();

    public async Task TrackAsync(AnalyticsEvent @event, CancellationToken cancellationToken = default)
    {
        if (_providers.Count == 0)
            return;

        var tasks = _providers.Select(p => TrackSafeAsync(p, @event, cancellationToken));
        await Task.WhenAll(tasks);
    }

    private async Task TrackSafeAsync(IAnalyticsProvider provider, AnalyticsEvent @event, CancellationToken cancellationToken)
    {
        try
        {
            await provider.TrackEventAsync(@event, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal cancellation path; do not log as provider failure.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Analytics provider {Provider} failed to track event {EventType} for session {SessionId}.",
                provider.GetType().Name, @event.EventType, @event.SessionId);
        }
    }
}
