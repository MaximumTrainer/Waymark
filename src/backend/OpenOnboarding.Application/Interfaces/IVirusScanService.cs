namespace OpenOnboarding.Application.Interfaces;

public interface IVirusScanService
{
    Task<ScanResult> ScanAsync(Stream stream, CancellationToken ct = default);
}
