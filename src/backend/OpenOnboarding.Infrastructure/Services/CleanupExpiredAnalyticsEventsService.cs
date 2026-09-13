using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenOnboarding.Application.Interfaces;

namespace OpenOnboarding.Infrastructure.Services;

/// <summary>
/// Prunes analytics events past their retention window, mirroring
/// <see cref="CleanupExpiredDocumentsService"/>. Registered only when the durable provider is on,
/// since there is nothing to prune otherwise.
/// </summary>
public sealed class CleanupExpiredAnalyticsEventsService(
    IServiceScopeFactory scopeFactory,
    ILogger<CleanupExpiredAnalyticsEventsService> logger,
    IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retentionDays = configuration.GetValue("Analytics:RetentionDays", 365);
        var intervalHours = configuration.GetValue("Analytics:CleanupIntervalHours", 24);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunCleanupAsync(retentionDays, stoppingToken);

            await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>Exposed as public for testability.</summary>
    public async Task RunCleanupAsync(int retentionDays, CancellationToken stoppingToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IAnalyticsEventStore>();

            var threshold = DateTimeOffset.UtcNow.AddDays(-retentionDays);
            var deleted = await store.DeleteRecordedBeforeAsync(threshold, stoppingToken);

            if (deleted > 0)
                logger.LogInformation(
                    "Deleted {Count} analytics event(s) older than {Days} days", deleted, retentionDays);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during analytics event cleanup");
        }
    }
}
