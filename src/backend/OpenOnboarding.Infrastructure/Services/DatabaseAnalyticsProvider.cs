using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Infrastructure.Persistence;

namespace OpenOnboarding.Infrastructure.Services;

/// <summary>
/// Durable analytics sink: writes every dispatched event to the database so the per-session trail
/// can be queried and exported.
/// <para>
/// Resolves its own scope because it is a singleton sink invoked from background dispatch, while
/// the DbContext is scoped.
/// </para>
/// </summary>
public sealed class DatabaseAnalyticsProvider(IServiceScopeFactory scopeFactory) : IAnalyticsProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task TrackEventAsync(AnalyticsEvent @event, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OnboardingDbContext>();

        db.AnalyticsEvents.Add(new AnalyticsEventRecord
        {
            Id = @event.EventId,
            EventType = @event.EventType,
            JourneyId = @event.JourneyId,
            SessionId = @event.SessionId,
            StepId = @event.StepId,
            StepIndex = @event.StepIndex,
            PayloadJson = JsonSerializer.Serialize(@event.Payload, JsonOptions),
            Source = @event.Source,
            OccurredAt = @event.OccurredAt,
            RecordedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
    }
}
