using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Infrastructure.Services;

namespace OpenOnboarding.Application.Tests;

public sealed class CleanupExpiredDocumentsServiceTests
{
    private static CleanupExpiredDocumentsService BuildService(
        FakeDocumentStorageService storageService,
        int retentionDays = 90,
        int intervalHours = 24)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DocumentStorage:RetentionDays"] = retentionDays.ToString(),
                ["DocumentStorage:CleanupIntervalHours"] = intervalHours.ToString()
            })
            .Build();

        var scopeFactory = new FakeServiceScopeFactory(storageService);
        return new CleanupExpiredDocumentsService(scopeFactory, NullLogger<CleanupExpiredDocumentsService>.Instance, config);
    }

    [Fact]
    public async Task ExecuteAsync_DeletesDocumentsOlderThanRetentionDays()
    {
        var storage = new FakeDocumentStorageService();

        var now = DateTimeOffset.UtcNow;
        storage.AddFile("old-file", now.AddDays(-100));
        storage.AddFile("recent-file", now.AddDays(-5));

        var svc = BuildService(storage, retentionDays: 90);

        using var cts = new CancellationTokenSource();
        var task = svc.StartAsync(cts.Token);

        // Give it a moment to run the first cleanup pass
        await Task.Delay(200);
        await cts.CancelAsync();

        try { await task; } catch (OperationCanceledException) { }

        Assert.Contains("old-file", storage.DeletedFiles);
        Assert.DoesNotContain("recent-file", storage.DeletedFiles);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotDeleteDocumentsWithinRetentionPeriod()
    {
        var storage = new FakeDocumentStorageService();

        var now = DateTimeOffset.UtcNow;
        storage.AddFile("fresh-file", now.AddDays(-10));

        var svc = BuildService(storage, retentionDays: 90);

        using var cts = new CancellationTokenSource();
        var task = svc.StartAsync(cts.Token);

        await Task.Delay(200);
        await cts.CancelAsync();

        try { await task; } catch (OperationCanceledException) { }

        Assert.DoesNotContain("fresh-file", storage.DeletedFiles);
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    internal sealed class FakeDocumentStorageService : IDocumentStorageService
    {
        private readonly Dictionary<string, StoredFileInfo> _files = new();
        public List<string> DeletedFiles { get; } = new();

        public void AddFile(string fileId, DateTimeOffset storedAt)
        {
            _files[fileId] = new StoredFileInfo(fileId, fileId + ".txt", "text/plain", 10, storedAt);
        }

        public Task<StoredFileInfo> StoreAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<(Stream Stream, StoredFileInfo Info)> GetStreamAsync(string fileId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<ScanResult> ScanAsync(string fileId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task DeleteAsync(string fileId, CancellationToken cancellationToken = default)
        {
            DeletedFiles.Add(fileId);
            _files.Remove(fileId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<StoredFileInfo>> ListOlderThanAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default)
        {
            var result = _files.Values.Where(f => f.StoredAt < threshold).ToList();
            return Task.FromResult<IReadOnlyList<StoredFileInfo>>(result);
        }
    }

    private sealed class FakeServiceScopeFactory(FakeDocumentStorageService storage) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new FakeServiceScope(storage);
    }

    private sealed class FakeServiceScope(FakeDocumentStorageService storage) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = new FakeServiceProvider(storage);
        public void Dispose() { }
    }

    private sealed class FakeServiceProvider(FakeDocumentStorageService storage) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(IDocumentStorageService) ? storage : null;
    }
}
