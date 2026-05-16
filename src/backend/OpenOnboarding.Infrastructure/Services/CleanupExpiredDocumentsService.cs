using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenOnboarding.Application.Interfaces;

namespace OpenOnboarding.Infrastructure.Services;

public sealed class CleanupExpiredDocumentsService(
    IServiceScopeFactory scopeFactory,
    ILogger<CleanupExpiredDocumentsService> logger,
    IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retentionDays = configuration.GetValue("DocumentStorage:RetentionDays", 90);
        var intervalHours = configuration.GetValue("DocumentStorage:CleanupIntervalHours", 24);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunCleanupAsync(retentionDays, stoppingToken);

            await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunCleanupAsync(int retentionDays, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorageService>();

            var threshold = DateTimeOffset.UtcNow.AddDays(-retentionDays);
            var expired = await storage.ListOlderThanAsync(threshold, stoppingToken);

            foreach (var file in expired)
            {
                await storage.DeleteAsync(file.FileId, stoppingToken);
            }

            if (expired.Count > 0)
                logger.LogInformation("Deleted {count} expired document(s) older than {days} days", expired.Count, retentionDays);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during document cleanup");
        }
    }
}
