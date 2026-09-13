using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Infrastructure.Persistence;
using OpenOnboarding.Infrastructure.Services;

namespace OpenOnboarding.Application.Tests;

/// <summary>
/// Analytics rows accumulate on every step of every session, so they need a retention window the
/// way stored documents do.
/// </summary>
public sealed class AnalyticsRetentionTests
{
    private static OnboardingDbContext BuildDbContext()
        => new(new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static AnalyticsEventRecord Event(string sessionId, DateTimeOffset recordedAt) => new()
    {
        EventType = "step_viewed",
        JourneyId = "journey-1",
        SessionId = sessionId,
        RecordedAt = recordedAt,
        OccurredAt = recordedAt
    };

    private static CleanupExpiredAnalyticsEventsService BuildService(OnboardingDbContext db)
        => new(
            new TestServiceScopeFactory(db),
            NullLogger<CleanupExpiredAnalyticsEventsService>.Instance,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build());

    [Fact]
    public async Task Cleanup_RemovesEventsPastTheRetentionWindow_AndKeepsTheRest()
    {
        using var db = BuildDbContext();
        db.AnalyticsEvents.AddRange(
            Event("old-1", DateTimeOffset.UtcNow.AddDays(-400)),
            Event("old-2", DateTimeOffset.UtcNow.AddDays(-366)),
            Event("recent", DateTimeOffset.UtcNow.AddDays(-10)));
        await db.SaveChangesAsync();

        await BuildService(db).RunCleanupAsync(retentionDays: 365);

        var remaining = await db.AnalyticsEvents.Select(e => e.SessionId).ToListAsync();
        Assert.Equal(["recent"], remaining);
    }

    [Fact]
    public async Task Cleanup_LeavesEverythingWhenNothingHasExpired()
    {
        using var db = BuildDbContext();
        db.AnalyticsEvents.Add(Event("recent", DateTimeOffset.UtcNow.AddDays(-1)));
        await db.SaveChangesAsync();

        await BuildService(db).RunCleanupAsync(retentionDays: 365);

        Assert.Equal(1, await db.AnalyticsEvents.CountAsync());
    }

    [Fact]
    public async Task Retention_MeasuresWriteTimeNotTheClientReportedTimestamp()
    {
        // A client controls OccurredAt and could backdate it; deleting on that would let a caller
        // evict its own trail early.
        using var db = BuildDbContext();
        db.AnalyticsEvents.Add(new AnalyticsEventRecord
        {
            EventType = "step_viewed",
            JourneyId = "journey-1",
            SessionId = "backdated",
            OccurredAt = DateTimeOffset.UtcNow.AddYears(-10),
            RecordedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        await BuildService(db).RunCleanupAsync(retentionDays: 365);

        Assert.Equal(1, await db.AnalyticsEvents.CountAsync());
    }

    [Fact]
    public async Task Store_ReturnsOnlyTheRequestedSessionsTrail()
    {
        using var db = BuildDbContext();
        db.AnalyticsEvents.AddRange(
            Event("session-a", DateTimeOffset.UtcNow.AddMinutes(-2)),
            Event("session-b", DateTimeOffset.UtcNow.AddMinutes(-1)));
        await db.SaveChangesAsync();

        var trail = await new DatabaseAnalyticsEventStore(db).GetSessionTrailAsync("session-a");

        var only = Assert.Single(trail);
        Assert.Equal("session-a", only.SessionId);
    }

    private sealed class TestServiceScopeFactory(OnboardingDbContext db) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new TestServiceScope(db);
    }

    private sealed class TestServiceScope(OnboardingDbContext db) : IServiceScope, IAsyncDisposable
    {
        public IServiceProvider ServiceProvider { get; } = new TestServiceProvider(db);
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestServiceProvider(OnboardingDbContext db) : IServiceProvider, IAsyncDisposable
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(IAnalyticsEventStore) ? new DatabaseAnalyticsEventStore(db) : null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
