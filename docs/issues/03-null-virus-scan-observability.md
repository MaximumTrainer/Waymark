---
title: "NullVirusScanService — add monitoring and observability for bypassed scans"
labels: ["security", "observability"]
---

## Summary

When `VirusScan:Enabled` is `false` (the default), `NullVirusScanService` is registered instead of `ClamAvScanService`. The null implementation silently returns `ScanResult(true, null)` — i.e. "clean" — for every file without emitting any log, metric, or telemetry signal.

This means:
- Documents uploaded in a production environment with virus scanning disabled are accepted with no audit trail of the bypass.
- Operators have no way to detect misconfiguration (e.g. ClamAV disabled accidentally).

**Affected file:** `src/backend/OpenOnboarding.Infrastructure/Services/NullVirusScanService.cs`

### Current implementation

```csharp
public sealed class NullVirusScanService : IVirusScanService
{
    public Task<ScanResult> ScanAsync(Stream stream, CancellationToken ct = default) =>
        Task.FromResult(new ScanResult(true, null));
}
```

---

## Requirements

### 1 — Logging

Inject `ILogger<NullVirusScanService>` and emit a **warning** on every call:

```
Virus scanning is disabled (NullVirusScanService). File scan bypassed.
```

The log message must include the stream length (if seekable) so that operators can correlate bypassed scans with file size in log aggregation tooling.

### 2 — Metrics

Increment an `IMetricsService` counter for bypassed scans so the Prometheus dashboard can alert on unexpected volume:

```
metricsService.IncrementVirusScanBypassed();   // new counter to be added to IMetricsService
```

Add `IncrementVirusScanBypassed()` to `IMetricsService` and implement it in `PrometheusMetricsService` (counter name: `waymark_virus_scan_bypassed_total`).

### 3 — Startup warning

In `ServiceCollectionExtensions.AddInfrastructure`, after registering `NullVirusScanService`, log a **startup warning** via the host logger:

```
VirusScan:Enabled is false — virus scanning is disabled. Do not use this configuration in production.
```

### 4 — Tests

Add test cases in `DocumentUploadAndSseTests` (or a new `NullVirusScanServiceTests` class) to verify:
- The warning log is emitted when `NullVirusScanService.ScanAsync` is called.
- The metrics counter is incremented once per call.

---

## Acceptance Criteria

- [ ] `NullVirusScanService` logs a `LogLevel.Warning` message on every call.
- [ ] `NullVirusScanService` increments a `waymark_virus_scan_bypassed_total` Prometheus counter on every call.
- [ ] `IMetricsService` and `PrometheusMetricsService` implement `IncrementVirusScanBypassed()`.
- [ ] A startup warning is logged when `NullVirusScanService` is registered.
- [ ] Unit tests verify the log emission and counter increment.
- [ ] Existing virus-scanning tests continue to pass.
- [ ] `dotnet test` passes.
