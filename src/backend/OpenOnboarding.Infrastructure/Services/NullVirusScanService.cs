using Microsoft.Extensions.Logging;
using OpenOnboarding.Application.Interfaces;

namespace OpenOnboarding.Infrastructure.Services;

public sealed class NullVirusScanService : IVirusScanService
{
    private readonly ILogger<NullVirusScanService> _logger;
    private readonly IMetricsService _metrics;

    public NullVirusScanService(ILogger<NullVirusScanService> logger, IMetricsService metrics)
    {
        _logger = logger;
        _metrics = metrics;
        _logger.LogWarning(
            "Virus scanning is DISABLED (VirusScan:Enabled is not set or false). " +
            "NullVirusScanService will bypass all document scans. " +
            "Set VirusScan:Enabled=true and configure ClamAV for production use.");
    }

    public Task<ScanResult> ScanAsync(Stream stream, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "NullVirusScanService is active: document scan bypassed. " +
            "Enable ClamAV via VirusScan:Enabled=true for production security.");
        _metrics.IncrementVirusScanBypassed();
        return Task.FromResult(new ScanResult(true, null));
    }
}
