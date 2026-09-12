---
title: "Frontend API key grants full Operator access to every browser visitor"
labels: ["security", "authentication", "frontend"]
---

## Summary

The SPA authenticates to the backend with a **shared API key baked into the browser bundle** (`VITE_API_KEY`), and `ApiKeyAuthenticationHandler` maps any valid API key to a **full `Operator` principal**. Vite inlines `VITE_*` variables at build time, so the key ships as a literal string in the published JavaScript and is readable by anyone who loads the public onboarding page.

Any visitor can extract the key and call every Operator-scoped endpoint.

**Affected files:**
- `src/backend/OpenOnboarding.Api/Authentication/ApiKeyAuthenticationHandler.cs` (lines 34–39)
- `src/frontend/.env.example`
- `src/frontend/src/onboarding/api/workflow-api-client.ts` (line 19)
- `src/frontend/src/onboarding/hooks/useOnboarding.ts` (line 14), `useFlow.ts` (line 4)
- `src/frontend/src/App.tsx` (line 107), `builder/AdminJourneyBuilderPage.tsx` (line 6), `builder/FlowAuthoringPanel.tsx` (line 13)
- `src/frontend/src/sessions/SessionList.tsx`, `sessions/SessionDetail.tsx`, `webhooks/WebhookDeliveries.tsx`, `analytics/FlowAnalytics.tsx`, `flows/FlowVersionHistory.tsx`

### The handler grants Operator unconditionally

```csharp
var claims = new[]
{
    new Claim(ClaimTypes.NameIdentifier, "api-key-user"),
    new Claim(ClaimTypes.Name, "api-key-user"),
    new Claim(ClaimTypes.Role, AppRoles.Operator),
};
```

### What the leaked key unlocks

With the key alone (no SSO, no cookie) a caller can reach, among others:

| Endpoint | Policy | Exposure |
|---|---|---|
| `GET /api/workflow/sessions` | `OperatorOnly` | Every applicant session across every flow |
| `GET /api/workflow/sessions/{id}/submissions` | `OperatorOnly` | All submitted form data (PII) |
| `GET /api/workflow/flows/{id}/stats` | `OperatorOnly` | Business metrics |
| `POST`/`PUT`/`DELETE /api/flows` | Operator | Create, rewrite or delete journey definitions |
| `POST /api/webhooks` | Operator | Register an attacker-controlled webhook and exfiltrate future events |

The resource-based `SessionOwnershipHandler` short-circuits on `IsInRole(Operator)`, so per-session ownership checks provide no defence either.

The `Applicant` role and the `customerProfileId` ownership path already exist in the backend, but the shipped frontend has no way to obtain an applicant credential — it uses the Operator API key for applicant traffic.

---

## Requirements

1. Stop shipping a privileged credential to the browser. The public onboarding app must authenticate as an **applicant**, not an operator.
2. Introduce a per-session applicant credential issued by the backend at `POST /api/workflow/sessions/start` (e.g. a short-lived JWT carrying `role=Applicant` and `customerProfileId`), and have the SPA send that instead of `X-Api-Key`.
3. Treat `X-Api-Key` as a **server-to-server** credential only: document it as such, and consider binding it to a named integration principal rather than a blanket Operator role.
4. Operator-scoped UI must use the existing SAML `AdminSession` cookie, not an API key.
5. Remove `VITE_API_KEY` from `.env.example` and from all frontend source, or restrict it to a local-development-only escape hatch that is absent from production builds.

---

## Acceptance Criteria

- [ ] A production frontend build contains no API key string: `grep -r "$VITE_API_KEY" dist/` returns no matches.
- [ ] Starting a session and submitting a step succeed without any `X-Api-Key` header being sent from the browser.
- [ ] An applicant credential issued for session A returns `403 Forbidden` when used against session B's `GET /api/workflow/sessions/{id}` and `.../submit`.
- [ ] An applicant credential returns `403 Forbidden` from `GET /api/workflow/sessions`, `GET /api/workflow/sessions/{id}/submissions` and `GET /api/workflow/flows/{id}/stats`.
- [ ] Operator-scoped frontend views authenticate with the `AdminSession` cookie; removing the cookie makes them return `401`/`403` rather than succeeding via an API key.
- [ ] `src/frontend/.env.example` no longer instructs developers to place an Operator-equivalent API key in the browser bundle.
- [ ] Integration tests cover: applicant credential accepted for its own session; rejected for another session; rejected for every `OperatorOnly` endpoint.
- [ ] `docs/runbook.md` documents the applicant credential lifetime and the server-to-server-only status of `Authentication:ApiKey`.
