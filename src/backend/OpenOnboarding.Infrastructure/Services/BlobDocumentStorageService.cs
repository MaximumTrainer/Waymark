using OpenOnboarding.Application.Exceptions;
using OpenOnboarding.Application.Interfaces;

namespace OpenOnboarding.Infrastructure.Services;

internal sealed class BlobDocumentStorageService(
    IBlobContainerAdapter adapter,
    IVirusScanService virusScanService) : IDocumentStorageService
{
    private const string MetaFileName = "meta_filename";
    private const string MetaStoredAt = "meta_storedat";
    private const string MetaSizeBytes = "meta_sizebytes";

    public async Task<StoredFileInfo> StoreAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        var fileId = Guid.NewGuid().ToString("N");

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        var sizeBytes = buffer.Length;
        buffer.Position = 0;

        var storedAt = DateTimeOffset.UtcNow;
        var metadata = new Dictionary<string, string>
        {
            [MetaFileName] = fileName,
            [MetaStoredAt] = storedAt.ToString("O"),
            [MetaSizeBytes] = sizeBytes.ToString()
        };

        await adapter.UploadAsync(fileId, buffer, contentType, metadata, cancellationToken);

        return new StoredFileInfo(fileId, fileName, contentType, sizeBytes, storedAt);
    }

    public async Task<(Stream Stream, StoredFileInfo Info)> GetStreamAsync(string fileId, CancellationToken cancellationToken = default)
    {
        try
        {
            var (stream, metadata, sizeBytes) = await adapter.DownloadAsync(fileId, cancellationToken);
            var info = BuildInfo(fileId, metadata, sizeBytes);
            return (stream, info);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            throw new NotFoundException($"File '{fileId}' not found.");
        }
    }

    public async Task<ScanResult> ScanAsync(string fileId, CancellationToken cancellationToken = default)
    {
        try
        {
            var (stream, _, _) = await adapter.DownloadAsync(fileId, cancellationToken);
            await using (stream)
            {
                return await virusScanService.ScanAsync(stream, cancellationToken);
            }
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            throw new NotFoundException($"File '{fileId}' not found.");
        }
    }

    public async Task DeleteAsync(string fileId, CancellationToken cancellationToken = default)
    {
        await adapter.DeleteAsync(fileId, cancellationToken);
    }

    public async Task<IReadOnlyList<StoredFileInfo>> ListOlderThanAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default)
    {
        var results = new List<StoredFileInfo>();

        await foreach (var entry in adapter.ListAsync(cancellationToken))
        {
            if (!entry.Metadata.TryGetValue(MetaStoredAt, out var storedAtStr))
                continue;
            if (!DateTimeOffset.TryParse(storedAtStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var storedAt))
                continue;
            if (storedAt >= threshold)
                continue;

            var fileName = entry.Metadata.TryGetValue(MetaFileName, out var fn) ? fn : entry.Name;
            var sizeBytes = entry.SizeBytes ?? (entry.Metadata.TryGetValue(MetaSizeBytes, out var sb) && long.TryParse(sb, out var s) ? s : 0L);
            results.Add(new StoredFileInfo(entry.Name, fileName, "application/octet-stream", sizeBytes, storedAt));
        }

        return results;
    }

    private static StoredFileInfo BuildInfo(string fileId, IDictionary<string, string> metadata, long sizeBytes)
    {
        var fileName = metadata.TryGetValue(MetaFileName, out var fn) ? fn : fileId;
        var storedAt = metadata.TryGetValue(MetaStoredAt, out var s) && DateTimeOffset.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : DateTimeOffset.UtcNow;
        return new StoredFileInfo(fileId, fileName, "application/octet-stream", sizeBytes, storedAt);
    }
}
