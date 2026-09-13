using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OpenOnboarding.Infrastructure.Services;

/// <summary>
/// Warns at startup when a non-Development environment is running the single-instance session
/// event emitter. The failure it guards against is silent — an SSE stream held by another replica
/// simply never receives progress — so it is worth naming loudly in the logs of any deployment
/// that might scale out.
/// </summary>
public sealed class InMemorySessionEventEmitterWarning(
    IHostEnvironment environment,
    ILogger<InMemorySessionEventEmitterWarning> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            logger.LogWarning(
                "Session events are using the in-memory emitter in the {Environment} environment. "
                + "SSE progress is delivered only by the instance that raised the event, so running "
                + "more than one replica will silently stop applicants receiving step progress. "
                + "Set SessionEvents:Transport=rabbitmq for multi-replica deployments.",
                environment.EnvironmentName);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
