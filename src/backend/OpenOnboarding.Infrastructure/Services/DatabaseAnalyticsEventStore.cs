using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Infrastructure.Persistence;

namespace OpenOnboarding.Infrastructure.Services;

public sealed class DatabaseAnalyticsEventStore(OnboardingDbContext db) : IAnalyticsEventStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<AnalyticsEvent>> GetSessionTrailAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var records = await db.AnalyticsEvents
            .AsNoTracking()
            .Where(x => x.SessionId == sessionId)
            .OrderBy(x => x.OccurredAt)
            .ThenBy(x => x.RecordedAt)
            .ToListAsync(cancellationToken);

        return records.Select(record => new AnalyticsEvent
        {
            EventId = record.Id,
            EventType = record.EventType,
            JourneyId = record.JourneyId,
            SessionId = record.SessionId,
            StepId = record.StepId,
            StepIndex = record.StepIndex,
            Payload = DeserializePayload(record.PayloadJson),
            OccurredAt = record.OccurredAt,
            Source = record.Source
        }).ToList();
    }

    public async Task<int> DeleteRecordedBeforeAsync(
        DateTimeOffset threshold,
        CancellationToken cancellationToken = default)
    {
        var expired = await db.AnalyticsEvents
            .Where(x => x.RecordedAt < threshold)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0)
            return 0;

        db.AnalyticsEvents.RemoveRange(expired);
        await db.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }

    private static IReadOnlyDictionary<string, object?> DeserializePayload(string payloadJson)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadJson, JsonOptions)
                   ?? new Dictionary<string, object?>();
        }
        catch (JsonException)
        {
            // A malformed payload must not make the whole trail unreadable.
            return new Dictionary<string, object?>();
        }
    }
}
