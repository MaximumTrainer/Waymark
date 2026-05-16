---
title: "Add cloud/blob storage adapter for document storage (production readiness)"
labels: ["enhancement", "infrastructure"]
---

## Summary

`LocalDocumentStorageService` stores uploaded documents on the local filesystem under `{webroot}/uploads/`. This is suitable for local development but is not viable in production because:

- Files are lost when the container/pod restarts.
- Horizontal scaling is impossible without a shared filesystem.
- There is no access-control layer, content-addressable deduplication, or server-side encryption.

The port (`IDocumentStorageService`) already exists; only a cloud adapter is missing.

**Affected file:** `src/backend/OpenOnboarding.Infrastructure/Services/LocalDocumentStorageService.cs`
**Port:** `src/backend/OpenOnboarding.Application/Interfaces/IDocumentStorageService.cs`

---

## Requirements

### 1 — New `BlobDocumentStorageService` adapter

Create `src/backend/OpenOnboarding.Infrastructure/Services/BlobDocumentStorageService.cs` implementing `IDocumentStorageService` using the Azure Blob Storage SDK (`Azure.Storage.Blobs`) or an S3-compatible SDK (`AWSSDK.S3` / `Minio`).

The implementation must:

- Use a configurable container/bucket name (`DocumentStorage:ContainerName`).
- Store metadata (file name, content type, size, uploaded-at) as blob metadata or a sidecar JSON object.
- Return a `StoredFileInfo` consistent with `LocalDocumentStorageService`.
- For `GetStreamAsync`, return a streaming (non-buffered) download so large files do not load fully into memory.
- For `ScanAsync`, stream the blob content to `IVirusScanService` without materialising the full file locally.

### 2 — Configuration-driven registration

In `ServiceCollectionExtensions.AddInfrastructure`, select the implementation based on `DocumentStorage:Provider`:

| Value | Implementation |
|-------|----------------|
| `"local"` (default) | `LocalDocumentStorageService` |
| `"azureblob"` | `BlobDocumentStorageService` (Azure) |
| `"s3"` | `BlobDocumentStorageService` (S3/Minio) |

Validate required configuration keys at startup (fail fast) using the existing `IStartupFilter` / validator pattern.

### 3 — File retention / expiry

Implement a `CleanupExpiredDocumentsService : BackgroundService` that:

- Runs on a configurable schedule (default: daily).
- Deletes documents older than a configurable retention period (`DocumentStorage:RetentionDays`, default: 90).
- Logs the count of deleted documents.
- Is guarded by a feature flag `DocumentStorage:EnableCleanup` (default `true`).

### 4 — Tests

- Unit tests for `BlobDocumentStorageService` using a mocked `BlobContainerClient`.
- Unit tests for `CleanupExpiredDocumentsService` using an in-memory/mock storage service.
- Integration test (optional, skipped in CI without credentials) against Azurite / LocalStack.

---

## Acceptance Criteria

- [ ] `BlobDocumentStorageService` implements `IDocumentStorageService` and passes the same behavioural tests as `LocalDocumentStorageService`.
- [ ] `DocumentStorage:Provider = "azureblob"` registers `BlobDocumentStorageService`; missing required config causes a startup exception with a descriptive message.
- [ ] `LocalDocumentStorageService` remains the default and all existing tests pass.
- [ ] `CleanupExpiredDocumentsService` deletes only documents older than `RetentionDays` and logs the count.
- [ ] Unit tests for both the storage adapter and cleanup service pass.
- [ ] `dotnet test` passes.
- [ ] No secrets or connection strings are hard-coded; all credentials come from configuration / environment variables.
