---
title: "WebhookService — CancellationToken not propagated through delivery retry loop"
labels: ["bug", "reliability"]
---

## Summary

`WebhookService.DeliverAsync` accepts a `CancellationToken` parameter but does **not** propagate it into the inner delivery loop or the `SaveChangesAsync` calls inside `DeliverToWebhookAsync`. This means:

1. A host shutdown signal (e.g. `SIGTERM`) cannot cleanly interrupt a delivery attempt.
2. Database save operations during the retry loop cannot be cancelled.
3. The `Task.Delay` inside the retry loop already uses `CancellationToken.None` explicitly, bypassing the caller's token.

**Affected file:** `src/backend/OpenOnboarding.Infrastructure/Services/WebhookService.cs`

### Current code (lines 94–133)

```csharp
private async Task DeliverToWebhookAsync(Webhook webhook, Guid sessionId, string eventType, string payloadJson)
// ↑ No CancellationToken parameter

    // ...
    for (var attempt = 0; attempt <= 2; attempt++)
    {
        if (attempt > 0)
            await _delay(delays[attempt - 1], CancellationToken.None);  // ← ignores caller token

        var result = await webhookHttpClient.SendAsync(webhook.Url, payloadJson, signature);
        // ...
        await dbContext.SaveChangesAsync();   // ← no cancellation token
    }

    delivery.Status = "failed";
    await dbContext.SaveChangesAsync();       // ← no cancellation token
```

---

## Requirements

### Fix 1 — Pass `CancellationToken` through the delivery pipeline

1. Add a `CancellationToken cancellationToken` parameter to `DeliverToWebhookAsync`.
2. Pass the token to all `SaveChangesAsync` calls: `SaveChangesAsync(cancellationToken)`.
3. Pass the token to `_delay`: `await _delay(delays[attempt - 1], cancellationToken)`.
4. Pass the token to `webhookHttpClient.SendAsync` if the interface supports it (add overload if needed).
5. In `DeliverAsync`, pass `cancellationToken` when calling `DeliverToWebhookAsync` for each webhook.

### Fix 2 — Handle `OperationCanceledException` gracefully

When cancellation is requested during a delivery attempt, the partially-recorded `WebhookDelivery` should be saved with `Status = "cancelled"` before the exception propagates, so the delivery log is not left in an indeterminate state.

```csharp
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
{
    delivery.Status = "cancelled";
    await dbContext.SaveChangesAsync(CancellationToken.None);  // Use None; host is shutting down
    throw;
}
```

### Tests to add

Add to `WebhookRetryTests` (or a new test class):

| Test | Expected outcome |
|------|-----------------|
| Cancelled before first attempt | `DeliverToWebhookAsync` exits immediately; delivery saved as `"cancelled"` |
| Cancelled during retry delay | Retry loop exits; delivery saved as `"cancelled"` |
| Normal delivery (existing tests) | Pass without modification |

---

## Acceptance Criteria

- [ ] `DeliverToWebhookAsync` signature includes `CancellationToken cancellationToken`.
- [ ] All `SaveChangesAsync` calls inside the delivery loop pass the token.
- [ ] Retry `Task.Delay` passes the token.
- [ ] On cancellation, the delivery record is persisted with `Status = "cancelled"`.
- [ ] Both new cancellation test cases are green.
- [ ] All existing `WebhookRetryTests` pass without modification.
- [ ] `dotnet test` passes.
