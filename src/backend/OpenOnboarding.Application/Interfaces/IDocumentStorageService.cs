namespace OpenOnboarding.Application.Interfaces;

public record StoredFileInfo(
    string FileId,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset StoredAt);

public record ScanResult(bool IsSafe, string? ThreatName);

public interface IDocumentStorageService
{
    Task<StoredFileInfo> StoreAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken = default);
    Task<(Stream Stream, StoredFileInfo Info)> GetStreamAsync(string fileId, CancellationToken cancellationToken = default);
    Task<ScanResult> ScanAsync(string fileId, CancellationToken cancellationToken = default);
    Task DeleteAsync(string fileId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredFileInfo>> ListOlderThanAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default);
}
