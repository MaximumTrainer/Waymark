---
title: "Operator-only UI is rendered on the unauthenticated applicant route"
labels: ["security", "frontend"]
---

## Summary

`App.tsx` guards **only** `/admin/journey-builder` behind the SSO session check. The root route `/` — the page an applicant loads to complete onboarding — renders the full operator toolset below the step renderer with no authentication gate at all.

**Affected file:** `src/frontend/src/App.tsx`

### The guard covers one route

```tsx
if (isAdminJourneyBuilderRoute && adminAuthState !== 'authorized') { ... }   // line 223
if (isAdminJourneyBuilderRoute && adminAuthState === 'authorized') { ... }   // line 231
```

Everything after that falls through to the applicant view, which renders:

| Component | Line | What it exposes |
|---|---|---|
| `FlowAuthoringPanel` | 320 | Create / edit / publish journey definitions |
| `FlowVersionHistory` | 328 | Version history and rollback controls |
| `FlowAnalytics` | 372 | Aggregate conversion and drop-off metrics |
| `SessionList` / `SessionDetail` | 384 | Every applicant's session, including submitted field data |
| `WebhookDeliveries` | 390 | Webhook endpoints and delivery payloads |

This is a separate defect from the credential problem in the API-key issue: even after the backend stops trusting a browser-held Operator key, these views would still render and issue operator API calls on the applicant's page. Their requests would then fail with `401`/`403`, leaving broken UI in front of applicants.

The applicant page also renders a "Journey" and "Persona" picker plus the Visual Journey Builder canvas, which let an applicant switch themselves onto any seeded flow.

---

## Requirements

1. Move every operator-facing section off the applicant route onto an authenticated route (e.g. extend `/admin` to host flow authoring, version history, analytics, sessions and webhook deliveries).
2. Apply the existing `adminAuthState === 'authorized'` check — which already requires an `Operator` role from `GET /api/auth/me` — to those routes.
3. Reduce the applicant route to the onboarding experience: the step renderer and its progress indication.
4. Replace the ad-hoc `window.location.pathname` routing with a single place that maps route → required role, so a new admin surface cannot be added without a guard.

---

## Acceptance Criteria

- [ ] Loading `/` with no `AdminSession` cookie renders only the onboarding step renderer — none of `FlowAuthoringPanel`, `FlowVersionHistory`, `FlowAnalytics`, `SessionList`, `SessionDetail` or `WebhookDeliveries` appear in the DOM.
- [ ] Loading `/` issues no request to any `OperatorOnly` endpoint (`/api/workflow/sessions`, `/api/workflow/flows/{id}/stats`, `/api/webhooks`).
- [ ] The journey and persona selectors are not present on the unauthenticated applicant route.
- [ ] Operator sections are reachable on an authenticated admin route and render there for a user whose `/api/auth/me` response includes the `Operator` role.
- [ ] A user without the `Operator` role who navigates directly to an admin route is redirected to `/login` and never sees operator data.
- [ ] Route → required-role mapping lives in one module covered by unit tests.
- [ ] Vitest tests assert the unauthenticated root render excludes each operator component; a Playwright spec covers the redirect for an unauthenticated admin route.
