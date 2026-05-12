using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenOnboarding.Domain.Enums;
using OpenOnboarding.Infrastructure.Persistence;

namespace OpenOnboarding.Infrastructure.Services;

public sealed class SessionTimeoutService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<SessionTimeoutService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timeoutMinutes = configuration.GetValue<int>("SessionTimeoutMinutes", 1440);
        if (timeoutMinutes <= 0)
        {
            logger.LogInformation("SessionTimeoutService is disabled (SessionTimeoutMinutes = {Value}).", timeoutMinutes);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
                await CheckAndAbandonAsync(timeoutMinutes, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error during session timeout check.");
            }
        }
    }

    /// <summary>
    /// Finds all sessions in the Started state whose last update is older than
    /// <paramref name="timeoutMinutes"/> and transitions them to Abandoned.
    /// Exposed as public for testability.
    /// </summary>
    public async Task CheckAndAbandonAsync(int timeoutMinutes, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OnboardingDbContext>();

        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-timeoutMinutes);

        var timedOut = await dbContext.Sessions
            .Where(x => x.Status == SessionStatus.Started && x.UpdatedAt < cutoff)
            .ToListAsync(cancellationToken);

        foreach (var session in timedOut)
        {
            session.Status = SessionStatus.Abandoned;
            session.UpdatedAt = DateTimeOffset.UtcNow;
        }

        if (timedOut.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Auto-abandoned {Count} timed-out session(s).", timedOut.Count);
        }
    }
}
