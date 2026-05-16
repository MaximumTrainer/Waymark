---
title: "RabbitMqEventBus — fix sync-over-async startup registration and add resilience"
labels: ["bug", "reliability", "infrastructure"]
---

## Summary

Two related problems exist in the RabbitMQ event bus integration:

### Problem 1 — Sync-over-async in DI startup (deadlock risk)

`ServiceCollectionExtensions.AddInfrastructure` uses `.GetAwaiter().GetResult()` to block on the async factory:

```csharp
// src/backend/OpenOnboarding.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs:84
services.AddSingleton<IEventBus>(sp =>
    RabbitMqEventBus.CreateAsync(rabbitUri, exchange, sp.GetRequiredService<ILogger<RabbitMqEventBus>>())
        .GetAwaiter().GetResult());   // ← sync-over-async antipattern
```

This blocks a thread-pool thread during startup and can cause a deadlock on runtimes that synchronise the thread pool (e.g. some ASP.NET Core startup contexts).

### Problem 2 — No resilience or reconnection

If RabbitMQ is temporarily unavailable after startup, `PublishAsync` will throw and the event is silently lost (the exception is re-thrown and logged, but the caller — `SubmitStepCommandHandler` — has no retry or fallback). There is no circuit breaker, no reconnection, and no dead-letter handling.

**Affected files:**
- `src/backend/OpenOnboarding.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- `src/backend/OpenOnboarding.Infrastructure/EventBus/RabbitMqEventBus.cs`

---

## Requirements

### Fix 1 — Remove sync-over-async in startup

Replace the blocking singleton factory with an `IHostedService` initializer pattern:

1. Create `RabbitMqEventBusInitializer : IHostedService` that connects in `StartAsync` (async) and stores the `RabbitMqEventBus` instance in a shared holder.
2. Register a lightweight `IEventBus` proxy that delegates to the holder and throws an `InvalidOperationException` with a clear message if called before initialization completes.
3. Alternatively, use `IHostApplicationBuilder.Services.AddHostedService` + `Lazy<Task<IEventBus>>` with `IServiceProvider.GetRequiredService` deferred to first use.

The chosen approach must not use `.GetAwaiter().GetResult()` or `.Result` anywhere in the startup path.

### Fix 2 — Resilient publish with Polly

Add retry-with-backoff around `BasicPublishAsync` using **Polly** (already a transitive dependency):

- 3 retry attempts with exponential back-off (1 s, 2 s, 4 s).
- On exhausted retries, log the failure at `Error` level and **do not rethrow** (fire-and-forget events should not fail the caller's transaction).
- Add a `bool ThrowOnPublishFailure` option (default `false`) for contexts that require strong delivery guarantees.

### Fix 3 — Unit tests for RabbitMqEventBus

Add `RabbitMqEventBusTests` in `OpenOnboarding.Application.Tests` using a mocked `IChannel` / `IConnection`:

| Test | Scenario |
|------|----------|
| Happy path | `PublishAsync` serialises notification to JSON and calls `BasicPublishAsync` once |
| Transient failure + retry | First call throws; second call succeeds; verify 2 total calls |
| Exhausted retries | All 3 attempts throw; verify `LogError` is called; verify no exception escapes |
| Routing key | Routing key equals `notification.GetType().Name` |

---

## Acceptance Criteria

- [ ] No `.GetAwaiter().GetResult()` or `.Result` call remains in the RabbitMQ startup path.
- [ ] API starts successfully even when RabbitMQ is temporarily unreachable (connection failure is logged; application continues with degraded event publishing).
- [ ] `PublishAsync` retries up to 3 times on transient channel exceptions before logging at `Error` level.
- [ ] `RabbitMqEventBusTests` exists with the 4 test cases listed above, all green.
- [ ] `dotnet test` passes.
- [ ] Default credentials `amqp://guest:guest@localhost:5672/` are not hard-coded; configuration falls back to a documented default only when `EventBus:RabbitMq:Uri` is absent.
