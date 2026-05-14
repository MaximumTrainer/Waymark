using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using OpenOnboarding.Application.Exceptions;
using OpenOnboarding.Application.Interfaces;

namespace OpenOnboarding.Infrastructure.Services;

public sealed class LocalDocumentStorageService : IDocumentStorageService
{
    private readonly string _basePath;

    private readonly IVirusScanService _virusScanService;

    public LocalDocumentStorageService(IWebHostEnvironment env, IVirusScanService virusScanService)
    {
        _basePath = Path.Combine(
            env.WebRootPath ?? env.ContentRootPath,
            "uploads");
        _virusScanService = virusScanService;
    }

    public async Task<StoredFileInfo> StoreAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        var fileId = Guid.NewGuid().ToString("N");
        var dir = Path.Combine(_basePath, fileId[..2]);
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, fileId);

        long sizeBytes;
        await using (var fs = File.Create(filePath))
        {
            await stream.CopyToAsync(fs, cancellationToken);
            sizeBytes = fs.Length;
        }

        var info = new StoredFileInfo(fileId, fileName, contentType, sizeBytes, DateTimeOffset.UtcNow);
        var metaPath = filePath + ".meta.json";
        await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(info), cancellationToken);

        return info;
    }

    public async Task<(Stream Stream, StoredFileInfo Info)> GetStreamAsync(string fileId, CancellationToken cancellationToken = default)
    {
        if (fileId.Length < 2)
            throw new NotFoundException($"File '{fileId}' not found.");

        var dir = Path.Combine(_basePath, fileId[..2]);
        var filePath = Path.Combine(dir, fileId);
        var metaPath = filePath + ".meta.json";

        if (!File.Exists(filePath) || !File.Exists(metaPath))
            throw new NotFoundException($"File '{fileId}' not found.");

        var metaJson = await File.ReadAllTextAsync(metaPath, cancellationToken);
        var info = JsonSerializer.Deserialize<StoredFileInfo>(metaJson)!;
        var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return (stream, info);
    }

    public async Task<ScanResult> ScanAsync(string fileId, CancellationToken cancellationToken = default)
    {
        if (fileId.Length >= 2)
        {
            var filePath = Path.Combine(_basePath, fileId[..2], fileId);
            if (File.Exists(filePath))
            {
                await using var stream = File.OpenRead(filePath);
                return await _virusScanService.ScanAsync(stream, cancellationToken);
            }
        }
        return await _virusScanService.ScanAsync(Stream.Null, cancellationToken);
    }
}