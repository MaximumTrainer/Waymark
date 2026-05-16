---
title: "Add unit tests for SessionTimeoutService"
labels: ["testing", "reliability"]
---

## Summary

`SessionTimeoutService` is a `BackgroundService` that periodically marks stale sessions as `Abandoned`. It is registered as a hosted service in production but has **zero unit tests**. The core logic (`CheckAndAbandonAsync`) is already `public` and designed for testability (the method signature even says "Exposed as public for testability"), yet no test file exists.

**Affected file:** `src/backend/OpenOnboarding.Infrastructure/Services/SessionTimeoutService.cs`

### Current implementation (lines 50–72)

```csharp
/// <summary>
/// Finds all sessions in the Started state whose last update is older than
/// <paramref name="timeoutMinutes"/> and transitions them to Abandoned.
/// Exposed as public for testability.
/// </summary>
public async Task CheckAndAbandonAsync(int timeoutMinutes, CancellationToken cancellationToken = default)
{
    await using var scope = scopeFactory.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<OnboardingDbContext>();

    var cutoff = DateTimeOffset.UtcNow.AddMinutes(-timeoutMinutes);

    var timedOut = await dbContext.Sessions
        .Where(x => x.Status == SessionStatus.Started && x.UpdatedAt < cutoff)
        .ToListAsync(cancellationToken);

    foreach (var session in timedOut)
    {
        session.Status = SessionStatus.Abandoned;
        session.UpdatedAt = DateTimeOffset.UtcNow;
    }

    if (timedOut.Count > 0)
    {
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Auto-abandoned {Count} timed-out session(s).", timedOut.Count);
    }
}
```

---

## Requirements

Add a new test class `SessionTimeoutServiceTests` in `src/backend/OpenOnboarding.Application.Tests/` using the same `WebApplicationFactory`-based pattern as the existing tests (e.g. `WorkflowServiceTests`).

### Test cases required

| # | Scenario | Expected outcome |
|---|----------|-----------------|
| 1 | Session updated more than `timeoutMinutes` ago with status `Started` | Status transitions to `Abandoned`, `UpdatedAt` is refreshed |
| 2 | Session updated within `timeoutMinutes` ago with status `Started` | Session is **not** changed |
| 3 | Session with status `Completed` that is older than the timeout | Session is **not** changed |
| 4 | Session with status `Abandoned` that is older than the timeout | Session is **not** changed |
| 5 | Multiple sessions — mix of stale and fresh — in a single call | Only stale `Started` sessions are abandoned |
| 6 | `timeoutMinutes = 0` (service disabled) | `CheckAndAbandonAsync` is never invoked (service exits immediately) — verify via log or mock |
| 7 | `CancellationToken` cancelled mid-run | Method exits without throwing `OperationCanceledException` to the caller |

### Additional coverage

- Verify the `ILogger` message `"Auto-abandoned {Count} timed-out session(s)."` is emitted exactly once when sessions are abandoned (use `ILogger` capture or a mock logger).
- Verify **no** save or log call is made when there are no stale sessions (performance guard).

---

## Acceptance Criteria

- [ ] `SessionTimeoutServiceTests` exists in `OpenOnboarding.Application.Tests`.
- [ ] All 7 scenarios in the table above have a passing test.
- [ ] Logger output is verified for the abandon-success and no-sessions paths.
- [ ] Tests use an in-memory or SQLite test database (same pattern as existing integration tests) — no real Postgres required.
- [ ] `dotnet test` passes with all new tests green.
- [ ] No existing tests are modified or removed.
