using OpenOnboarding.Application.Interfaces;

namespace OpenOnboarding.Infrastructure.Services;

public sealed class NullVirusScanService : IVirusScanService
{
    public Task<ScanResult> ScanAsync(Stream stream, CancellationToken ct = default) =>
        Task.FromResult(new ScanResult(true, null));
}
