using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace OpenOnboarding.Infrastructure.Services;

internal sealed class AzureBlobContainerAdapter(BlobContainerClient containerClient) : IBlobContainerAdapter
{
    public async Task UploadAsync(string blobName, Stream content, string contentType, IDictionary<string, string> metadata, CancellationToken ct = default)
    {
        var blobClient = containerClient.GetBlobClient(blobName);
        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            Metadata = metadata
        };
        await blobClient.UploadAsync(content, options, ct);
    }

    public async Task<(Stream Stream, IDictionary<string, string> Metadata, long SizeBytes)> DownloadAsync(string blobName, CancellationToken ct = default)
    {
        var blobClient = containerClient.GetBlobClient(blobName);
        var response = await blobClient.DownloadStreamingAsync(cancellationToken: ct);
        var stream = response.Value.Content;
        var metadata = (IDictionary<string, string>)(response.Value.Details.Metadata ?? new Dictionary<string, string>());
        var sizeBytes = response.Value.Details.ContentLength;
        return (stream, metadata, sizeBytes);
    }

    public async Task DeleteAsync(string blobName, CancellationToken ct = default)
    {
        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.DeleteIfExistsAsync(cancellationToken: ct);
    }

    public async IAsyncEnumerable<BlobEntry> ListAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var item in containerClient.GetBlobsAsync(BlobTraits.Metadata, cancellationToken: ct))
        {
            yield return new BlobEntry(
                item.Name,
                item.Metadata ?? new Dictionary<string, string>(),
                item.Properties.LastModified,
                item.Properties.ContentLength);
        }
    }

    public async Task EnsureContainerExistsAsync(CancellationToken ct = default)
    {
        await containerClient.CreateIfNotExistsAsync(cancellationToken: ct);
    }
}
