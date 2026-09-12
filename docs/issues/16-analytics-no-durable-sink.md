---
title: "Journey analytics events have no durable sink and browser events never reach the backend"
labels: ["enhancement", "observability"]
---

## Summary

The analytics pipeline is fully built on both sides but terminates in a log statement on the backend and in `console` on the frontend. No journey analytics event is ever persisted or exported, and browser-side events never leave the browser at all — there is no ingestion endpoint to receive them.

**Affected files:**
- `src/backend/OpenOnboarding.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs` (lines 79–82)
- `src/backend/OpenOnboarding.Infrastructure/Services/ConsoleAnalyticsProvider.cs`
- `src/frontend/src/analytics/consoleAnalyticsSink.ts`
- `src/frontend/src/analytics/JourneyAnalyticsContext.tsx`

### Backend: one sink, and it writes to the logger

```csharp
if (configuration.GetValue("Analytics:ConsoleProvider:Enabled", true))
    services.AddSingleton<IAnalyticsProvider, ConsoleAnalyticsProvider>();

services.AddSingleton<ITelemetryService, TelemetryService>();
```

`TelemetryService` already fans out to every registered `IAnalyticsProvider` in parallel with per-provider error isolation, and `StartSessionCommandHandler` and `SubmitStepCommandHandler` already emit through it. The extension point works — nothing is plugged into it.

### Frontend: events are dispatched to a console sink only

`JourneyAnalyticsProvider` supports `registerSink` at runtime and `App.tsx` passes `initialSinks={[consoleAnalyticsSink]}`. Client-side events (step viewed, field interactions, journey abandonment) are therefore observable only in an open devtools console on the applicant's own machine.

### What still works

This is not a total absence of analytics: `SessionAnalyticsService` computes aggregate flow statistics directly from persisted sessions and submissions, and `GET /api/workflow/flows/{id}/stats` backs the `FlowAnalytics` view. What is missing is the **event stream** — per-step timing, drop-off points, and any client-side signal.

---

## Requirements

1. Add at least one durable `IAnalyticsProvider` — persist `AnalyticsEvent` rows to the database, or forward to an OpenTelemetry/OTLP collector — selected by configuration in the same style as `IEventBus` and `IVirusScanService`.
2. Add an ingestion endpoint (e.g. `POST /api/analytics/events`) that accepts batched client events, validates and rate-limits them, and dispatches them through `ITelemetryService`.
3. Ship an HTTP sink for the frontend that batches events and posts them to that endpoint, keeping the console sink for development.
4. Attribute client events to the authenticated session so they cannot be forged for arbitrary sessions.
5. Define a retention policy for stored events, consistent with the existing document-cleanup background service.

---

## Acceptance Criteria

- [ ] A durable analytics provider is registered by configuration and stores or forwards every event `TelemetryService` dispatches.
- [ ] Completing a journey produces a retrievable ordered event trail for that session, covering session start and each step submission.
- [ ] `POST /api/analytics/events` accepts a batch, rejects events whose session id does not match the caller's credential with `403`, and is rate-limited.
- [ ] The frontend HTTP sink batches events and survives endpoint failure without breaking the onboarding flow (failures are dropped or retried, never surfaced to the applicant).
- [ ] Disabling the durable provider by configuration leaves the application working with the console provider only.
- [ ] A retention policy is implemented and documented; stored events older than the configured window are removed.
- [ ] Unit tests cover provider registration by configuration, ingestion authorisation, and sink batching/failure handling.
