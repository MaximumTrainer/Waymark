using OpenOnboarding.Application.Interfaces;

namespace OpenOnboarding.Application.Tests.TestHelpers;

internal sealed class NoOpDocumentStorageService : IDocumentStorageService
{
    public Task<StoredFileInfo> StoreAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken = default)
        => Task.FromResult(new StoredFileInfo(Guid.NewGuid().ToString(), fileName, contentType, 0, DateTimeOffset.UtcNow));

    public Task<(Stream Stream, StoredFileInfo Info)> GetStreamAsync(string fileId, CancellationToken cancellationToken = default)
        => Task.FromResult<(Stream, StoredFileInfo)>((Stream.Null, new StoredFileInfo(fileId, "file", "application/octet-stream", 0, DateTimeOffset.UtcNow)));

    public Task<ScanResult> ScanAsync(string fileId, CancellationToken cancellationToken = default)
        => Task.FromResult(new ScanResult(true, null));

    public Task DeleteAsync(string fileId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<StoredFileInfo>> ListOlderThanAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<StoredFileInfo>>(Array.Empty<StoredFileInfo>());
}
