using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OpenOnboarding.Infrastructure.EventBus;

/// <summary>
/// Hosted service that asynchronously initialises the RabbitMQ connection on
/// startup, then wires the live bus into the DeferredEventBus singleton.
/// This removes the need for blocking .GetAwaiter().GetResult() calls during
/// DI registration.
/// </summary>
public sealed class RabbitMqEventBusInitializer(
    string connectionString,
    string exchangeName,
    DeferredEventBus deferredBus,
    ILogger<RabbitMqEventBus> busLogger) : IHostedService, IAsyncDisposable
{
    private RabbitMqEventBus? _bus;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _bus = await RabbitMqEventBus.CreateAsync(connectionString, exchangeName, busLogger);
        deferredBus.SetBus(_bus);
        busLogger.LogInformation("RabbitMQ event bus connected to exchange '{Exchange}'.", exchangeName);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_bus is not null)
            await _bus.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_bus is not null)
            await _bus.DisposeAsync();
    }
}
