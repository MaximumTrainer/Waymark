namespace OpenOnboarding.Infrastructure.Services;

internal record BlobEntry(
    string Name,
    IDictionary<string, string> Metadata,
    DateTimeOffset? LastModified,
    long? SizeBytes);

internal interface IBlobContainerAdapter
{
    Task UploadAsync(string blobName, Stream content, string contentType, IDictionary<string, string> metadata, CancellationToken ct = default);
    Task<(Stream Stream, IDictionary<string, string> Metadata, long SizeBytes)> DownloadAsync(string blobName, CancellationToken ct = default);
    Task DeleteAsync(string blobName, CancellationToken ct = default);
    IAsyncEnumerable<BlobEntry> ListAsync(CancellationToken ct = default);
    Task EnsureContainerExistsAsync(CancellationToken ct = default);
}
