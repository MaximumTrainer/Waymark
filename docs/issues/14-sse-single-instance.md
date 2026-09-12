---
title: "SSE session progress events break when the API runs more than one replica"
labels: ["bug", "reliability", "infrastructure"]
---

## Summary

`ISessionEventEmitter` has exactly one implementation, `InMemorySessionEventEmitter`, registered unconditionally as a singleton. It holds a `ConcurrentDictionary<Guid, Channel<SessionEvent>>` in **process memory**, so an event written on one API instance can only be read by a `GET /api/workflow/sessions/{id}/events` stream attached to that same instance.

Both CD workflows deploy to platforms that routinely run more than one replica — Azure Container Apps (`cd-azure.yml`) and Amazon ECS (`cd-aws.yml`). At two or more replicas, an applicant whose SSE stream is held by instance B silently receives nothing for a step submitted through instance A.

**Affected files:**
- `src/backend/OpenOnboarding.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs` (line 67)
- `src/backend/OpenOnboarding.Infrastructure/Services/InMemorySessionEventEmitter.cs`
- `src/backend/OpenOnboarding.Api/Controllers/WorkflowController.cs` (`GET sessions/{sessionId:guid}/events`, line 269)

### The registration has no distributed alternative

```csharp
services.AddSingleton<ISessionEventEmitter, InMemorySessionEventEmitter>();
```

Unlike `IEventBus` — which already selects `RabbitMqEventBus` when `RabbitMq:Host` is configured and falls back to `InMemoryEventBus` otherwise — there is no configuration-driven swap for the event emitter.

### Failure mode

The failure is silent. The stream stays open, the client sees no error, and progress simply never arrives; the applicant appears stuck until they reload and re-fetch state over HTTP. Sticky sessions would mask it only while the assigned instance survives — a scale-in or rolling deploy re-breaks it mid-journey.

RabbitMQ is already a dependency in `docker-compose.yml` and the infrastructure layer, so a fan-out adapter has a natural home.

---

## Requirements

1. Add a distributed `ISessionEventEmitter` adapter that fans an event out to every API instance — a RabbitMQ fanout exchange (reusing the existing connection management) or Redis pub/sub.
2. Select it by configuration the same way `IEventBus` is selected, keeping `InMemorySessionEventEmitter` as the single-instance and test default.
3. Preserve the existing semantics: per-session channels, the 100-event capacity bound, the completion/abandonment terminal events, and the stale-channel cleanup.
4. Document the deployment requirement — running more than one replica without a distributed emitter configured must be called out in the runbook.
5. Consider failing startup (or logging a prominent warning) when the in-memory emitter is active in a non-Development environment.

---

## Acceptance Criteria

- [ ] A distributed `ISessionEventEmitter` implementation exists and is registered when its transport is configured; `InMemorySessionEventEmitter` remains the default otherwise.
- [ ] An integration test starts two API hosts sharing one transport, opens the SSE stream on host B, emits an event through host A, and asserts host B's stream receives it.
- [ ] Terminal events (`session-completed`, `session-abandoned`) close the stream on every instance, not just the emitting one.
- [ ] Channel capacity limits and stale-channel cleanup behave identically for both implementations, covered by tests.
- [ ] A non-Development environment running the in-memory emitter logs a warning naming the scale-out limitation.
- [ ] `docs/runbook.md` documents the configuration keys and states that multi-replica deployments require the distributed emitter.
