using OpenOnboarding.Application.Exceptions;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Infrastructure.Services;

namespace OpenOnboarding.Application.Tests;

public sealed class BlobDocumentStorageServiceTests
{
    private static BlobDocumentStorageService BuildService(out FakeBlobContainerAdapter adapter, out FakeVirusScanService virusScan)
    {
        adapter = new FakeBlobContainerAdapter();
        virusScan = new FakeVirusScanService();
        return new BlobDocumentStorageService(adapter, virusScan);
    }

    [Fact]
    public async Task StoreAsync_ReturnsStoredFileInfo_WithCorrectProperties()
    {
        var svc = BuildService(out _, out _);
        var content = "hello world"u8.ToArray();
        using var stream = new MemoryStream(content);

        var info = await svc.StoreAsync(stream, "test.txt", "text/plain");

        Assert.Equal("test.txt", info.FileName);
        Assert.Equal("text/plain", info.ContentType);
        Assert.Equal(content.Length, info.SizeBytes);
        Assert.False(string.IsNullOrEmpty(info.FileId));
    }

    [Fact]
    public async Task StoreAsync_StoredDataCanBeRetrievedViaGetStreamAsync()
    {
        var svc = BuildService(out _, out _);
        var content = "blob content"u8.ToArray();
        using var stream = new MemoryStream(content);

        var info = await svc.StoreAsync(stream, "doc.pdf", "application/pdf");
        var (resultStream, resultInfo) = await svc.GetStreamAsync(info.FileId);

        await using (resultStream)
        {
            var buffer = new MemoryStream();
            await resultStream.CopyToAsync(buffer);
            Assert.Equal(content, buffer.ToArray());
        }

        Assert.Equal(info.FileId, resultInfo.FileId);
        Assert.Equal("doc.pdf", resultInfo.FileName);
    }

    [Fact]
    public async Task GetStreamAsync_WhenBlobNotFound_ThrowsNotFoundException()
    {
        var svc = BuildService(out _, out _);

        await Assert.ThrowsAsync<NotFoundException>(() => svc.GetStreamAsync("nonexistent"));
    }

    [Fact]
    public async Task ScanAsync_DelegatesToVirusScanService_WithBlobStream()
    {
        var svc = BuildService(out _, out var virusScan);
        var content = "scan me"u8.ToArray();
        using var stream = new MemoryStream(content);

        var info = await svc.StoreAsync(stream, "file.bin", "application/octet-stream");
        var result = await svc.ScanAsync(info.FileId);

        Assert.True(result.IsSafe);
        Assert.True(virusScan.WasCalled);
    }

    [Fact]
    public async Task DeleteAsync_RemovesBlobFromStorage()
    {
        var svc = BuildService(out _, out _);
        var content = "delete me"u8.ToArray();
        using var stream = new MemoryStream(content);

        var info = await svc.StoreAsync(stream, "temp.txt", "text/plain");
        await svc.DeleteAsync(info.FileId);

        await Assert.ThrowsAsync<NotFoundException>(() => svc.GetStreamAsync(info.FileId));
    }

    [Fact]
    public async Task ListOlderThanAsync_ReturnsOnlyBlobsOlderThanThreshold()
    {
        var svc = BuildService(out var adapter, out _);

        var now = DateTimeOffset.UtcNow;

        // Store an old file
        using var old = new MemoryStream("old"u8.ToArray());
        var oldInfo = await svc.StoreAsync(old, "old.txt", "text/plain");

        // Backdate the old file's metadata
        var oldMeta = adapter.Blobs[oldInfo.FileId].Metadata;
        oldMeta["meta_storedat"] = now.AddDays(-100).ToString("O");

        // Store a recent file
        using var recent = new MemoryStream("recent"u8.ToArray());
        var recentInfo = await svc.StoreAsync(recent, "recent.txt", "text/plain");

        var threshold = now.AddDays(-10);
        var list = await svc.ListOlderThanAsync(threshold);

        Assert.Single(list);
        Assert.Equal(oldInfo.FileId, list[0].FileId);
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    internal sealed class FakeBlobContainerAdapter : IBlobContainerAdapter
    {
        public Dictionary<string, (byte[] Content, IDictionary<string, string> Metadata)> Blobs { get; } = new();

        public async Task UploadAsync(string blobName, Stream content, string contentType, IDictionary<string, string> metadata, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            Blobs[blobName] = (ms.ToArray(), new Dictionary<string, string>(metadata));
        }

        public Task<(Stream Stream, IDictionary<string, string> Metadata, long SizeBytes)> DownloadAsync(string blobName, CancellationToken ct = default)
        {
            if (!Blobs.TryGetValue(blobName, out var entry))
                throw new Azure.RequestFailedException(404, "BlobNotFound");

            Stream stream = new MemoryStream(entry.Content);
            return Task.FromResult((stream, entry.Metadata, (long)entry.Content.Length));
        }

        public Task DeleteAsync(string blobName, CancellationToken ct = default)
        {
            Blobs.Remove(blobName);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<BlobEntry> ListAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var kvp in Blobs)
            {
                yield return new BlobEntry(kvp.Key, kvp.Value.Metadata, null, kvp.Value.Content.Length);
            }
            await Task.CompletedTask;
        }

        public Task EnsureContainerExistsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    internal sealed class FakeVirusScanService : IVirusScanService
    {
        public bool WasCalled { get; private set; }

        public Task<ScanResult> ScanAsync(Stream stream, CancellationToken ct = default)
        {
            WasCalled = true;
            return Task.FromResult(new ScanResult(true, null));
        }
    }
}
