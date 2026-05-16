---
title: "LocalDocumentStorageService — fix silent clean-result for missing files in ScanAsync"
labels: ["bug", "security"]
---

## Summary

`LocalDocumentStorageService.ScanAsync` contains a logic gap: if the requested file does not exist on disk, it falls through to calling `_virusScanService.ScanAsync(Stream.Null, cancellationToken)` and returns a **clean scan result** for a non-existent file.

This means:
1. A caller that provides an invalid or tampered `fileId` receives `ScanResult(true, null)` — "file is clean" — even though the file was never read.
2. The `NullVirusScanService` further amplifies this: in environments where virus scanning is disabled, *any* `fileId` value (including one that does not exist) is declared clean.

**Affected file:** `src/backend/OpenOnboarding.Infrastructure/Services/LocalDocumentStorageService.cs` (lines 61–73)

### Current code

```csharp
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
    // ← Silent fallthrough: passes Stream.Null when file is not found
    return await _virusScanService.ScanAsync(Stream.Null, cancellationToken);
}
```

---

## Requirements

### Fix — throw `NotFoundException` when file does not exist

Replace the silent fallthrough with the same `NotFoundException` that `GetStreamAsync` already throws:

```csharp
public async Task<ScanResult> ScanAsync(string fileId, CancellationToken cancellationToken = default)
{
    if (fileId.Length < 2)
        throw new NotFoundException($"File '{fileId}' not found.");

    var filePath = Path.Combine(_basePath, fileId[..2], fileId);
    if (!File.Exists(filePath))
        throw new NotFoundException($"File '{fileId}' not found.");

    await using var stream = File.OpenRead(filePath);
    return await _virusScanService.ScanAsync(stream, cancellationToken);
}
```

### Tests to add

Add the following test cases in `DocumentUploadAndSseTests` or a new `LocalDocumentStorageServiceTests` class:

| # | Scenario | Expected outcome |
|---|----------|-----------------|
| 1 | `ScanAsync` called with a valid `fileId` that was previously stored | Returns the scan result from `IVirusScanService` |
| 2 | `ScanAsync` called with a `fileId` that does not exist on disk | Throws `NotFoundException` |
| 3 | `ScanAsync` called with a `fileId` shorter than 2 characters | Throws `NotFoundException` |
| 4 | `ScanAsync` when `IVirusScanService` returns infected | Returns infected `ScanResult` to caller |

---

## Acceptance Criteria

- [ ] `ScanAsync` throws `NotFoundException` (not `ApplicationException` or similar) when the file does not exist.
- [ ] `ScanAsync` never calls `_virusScanService.ScanAsync(Stream.Null, ...)`.
- [ ] The 4 test cases above are implemented and green.
- [ ] `GetStreamAsync` and `StoreAsync` behaviour is unchanged.
- [ ] `dotnet test` passes.
