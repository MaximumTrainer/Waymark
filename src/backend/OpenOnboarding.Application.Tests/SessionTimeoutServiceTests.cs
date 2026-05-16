using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Domain.Enums;
using OpenOnboarding.Infrastructure.Persistence;
using OpenOnboarding.Infrastructure.Services;

namespace OpenOnboarding.Application.Tests;

/// <summary>
/// Unit tests for SessionTimeoutService.CheckAndAbandonAsync covering the 7 acceptance
/// criteria from GitHub issue #77.
/// </summary>
public sealed class SessionTimeoutServiceTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static OnboardingDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new OnboardingDbContext(options);
    }

    private static SessionTimeoutService BuildService(
        OnboardingDbContext db,
        int timeoutMinutes = 60)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SessionTimeoutMinutes"] = timeoutMinutes.ToString()
            })
            .Build();

        var scopeFactory = new TestServiceScopeFactory(db);
        return new SessionTimeoutService(scopeFactory, config, NullLogger<SessionTimeoutService>.Instance);
    }

    private static Flow BuildFlow()
        => new() { Name = "Timeout Test Flow" };

    private static Session BuildSession(Flow flow, SessionStatus status, DateTimeOffset updatedAt)
        => new()
        {
            FlowId = flow.Id,
            Flow = flow,
            Status = status,
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt
        };

    // ── Tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckAndAbandonAsync_AbandonsSessions_ExceedingTimeout()
    {
        // Scenario 1: Started sessions older than timeoutMinutes are moved to Abandoned
        var db = BuildDbContext();
        var flow = BuildFlow();
        db.Flows.Add(flow);
        var oldSession = BuildSession(flow, SessionStatus.Started, DateTimeOffset.UtcNow.AddMinutes(-120));
        db.Sessions.Add(oldSession);
        await db.SaveChangesAsync();

        var svc = BuildService(db, timeoutMinutes: 60);
        await svc.CheckAndAbandonAsync(60);

        var updated = await db.Sessions.FindAsync(oldSession.Id);
        Assert.Equal(SessionStatus.Abandoned, updated!.Status);
    }

    [Fact]
    public async Task CheckAndAbandonAsync_DoesNotAbandon_RecentSessions()
    {
        // Scenario 2: Started sessions within the timeout window are left alone
        var db = BuildDbContext();
        var flow = BuildFlow();
        db.Flows.Add(flow);
        var recentSession = BuildSession(flow, SessionStatus.Started, DateTimeOffset.UtcNow.AddMinutes(-30));
        db.Sessions.Add(recentSession);
        await db.SaveChangesAsync();

        var svc = BuildService(db, timeoutMinutes: 60);
        await svc.CheckAndAbandonAsync(60);

        var updated = await db.Sessions.FindAsync(recentSession.Id);
        Assert.Equal(SessionStatus.Started, updated!.Status);
    }

    [Fact]
    public async Task CheckAndAbandonAsync_DoesNotAbandon_CompletedSessions()
    {
        // Scenario 3: Completed sessions older than timeout are never touched
        var db = BuildDbContext();
        var flow = BuildFlow();
        db.Flows.Add(flow);
        var completedSession = BuildSession(flow, SessionStatus.Completed, DateTimeOffset.UtcNow.AddMinutes(-200));
        db.Sessions.Add(completedSession);
        await db.SaveChangesAsync();

        var svc = BuildService(db, timeoutMinutes: 60);
        await svc.CheckAndAbandonAsync(60);

        var updated = await db.Sessions.FindAsync(completedSession.Id);
        Assert.Equal(SessionStatus.Completed, updated!.Status);
    }

    [Fact]
    public async Task CheckAndAbandonAsync_DoesNotAbandon_AlreadyAbandonedSessions()
    {
        // Scenario 4: Sessions already in Abandoned status are not re-processed
        var db = BuildDbContext();
        var flow = BuildFlow();
        db.Flows.Add(flow);
        var abandonedSession = BuildSession(flow, SessionStatus.Abandoned, DateTimeOffset.UtcNow.AddMinutes(-200));
        db.Sessions.Add(abandonedSession);
        await db.SaveChangesAsync();

        var svc = BuildService(db, timeoutMinutes: 60);
        var before = abandonedSession.UpdatedAt;
        await svc.CheckAndAbandonAsync(60);

        var updated = await db.Sessions.FindAsync(abandonedSession.Id);
        Assert.Equal(SessionStatus.Abandoned, updated!.Status);
        Assert.Equal(before, updated.UpdatedAt); // unchanged
    }

    [Fact]
    public async Task CheckAndAbandonAsync_UpdatesUpdatedAt_ForAbandonedSessions()
    {
        // Scenario 5: UpdatedAt is refreshed to UtcNow when a session is abandoned
        var db = BuildDbContext();
        var flow = BuildFlow();
        db.Flows.Add(flow);
        var before = DateTimeOffset.UtcNow.AddMinutes(-200);
        var oldSession = BuildSession(flow, SessionStatus.Started, before);
        db.Sessions.Add(oldSession);
        await db.SaveChangesAsync();

        var svc = BuildService(db, timeoutMinutes: 60);
        var testStart = DateTimeOffset.UtcNow;
        await svc.CheckAndAbandonAsync(60);

        var updated = await db.Sessions.FindAsync(oldSession.Id);
        Assert.True(updated!.UpdatedAt >= testStart,
            $"Expected UpdatedAt >= {testStart} but was {updated.UpdatedAt}");
    }

    [Fact]
    public async Task CheckAndAbandonAsync_HandlesMultipleSessions_Independently()
    {
        // Scenario 6: Mix of old/recent sessions — only old ones are abandoned
        var db = BuildDbContext();
        var flow = BuildFlow();
        db.Flows.Add(flow);
        var oldSession = BuildSession(flow, SessionStatus.Started, DateTimeOffset.UtcNow.AddMinutes(-120));
        var recentSession = BuildSession(flow, SessionStatus.Started, DateTimeOffset.UtcNow.AddMinutes(-10));
        db.Sessions.AddRange(oldSession, recentSession);
        await db.SaveChangesAsync();

        var svc = BuildService(db, timeoutMinutes: 60);
        await svc.CheckAndAbandonAsync(60);

        Assert.Equal(SessionStatus.Abandoned, (await db.Sessions.FindAsync(oldSession.Id))!.Status);
        Assert.Equal(SessionStatus.Started, (await db.Sessions.FindAsync(recentSession.Id))!.Status);
    }

    [Fact]
    public async Task CheckAndAbandonAsync_WhenNoSessionsExist_DoesNotThrow()
    {
        // Scenario 7: Empty database — no exception is thrown
        var db = BuildDbContext();
        var svc = BuildService(db, timeoutMinutes: 60);

        var exception = await Record.ExceptionAsync(() => svc.CheckAndAbandonAsync(60));
        Assert.Null(exception);
    }

    // ── Test infrastructure ────────────────────────────────────────────────────

    /// <summary>
    /// Minimal IServiceScopeFactory that returns the shared test DbContext.
    /// </summary>
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
            => serviceType == typeof(OnboardingDbContext) ? db : null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
