# User Guide

This guide explains how to configure and test onboarding journeys for different user personas.

## 1. Persona model

The API uses role-based access control:
- `Operator`: manage flows, customers, sessions, and webhooks
- `Applicant`: run onboarding sessions, submit steps, and access owned sessions
- `ReadOnly`: read-only access to operator endpoints where permitted by policy

`X-Api-Key` maps a request to a full `Operator` principal. It is for server-to-server callers and
local API exploration only — **never send it from a browser**, where it would hand every visitor
operator access. Browser-based callers use the credentials in section 4 instead.

## 2. Create or reuse a flow

### Option A: Use seeded flow

At API startup, development seed data creates a flow with ID:
`11111111-1111-1111-1111-111111111111`

### Option B: Create a custom flow

Use:
- `POST /api/flows`
- `PUT /api/flows/{flowId}`
- `GET /api/flows/{flowId}`

Flow nodes support:
- `Form`
- `DocumentUpload`
- `Redirect`
- `Information`
- `Logic`

Connection conditions use operators from `ConditionOperator` (for example: `Equals`, `NotEquals`, `GreaterThan`, `Contains`).

## 3. Configure customer personas

Create representative customer profiles with:
- `POST /api/customers`

Use external IDs and metadata to model persona variants (for example: region, risk tier, business type).

## 4. Start and progress a session

### Start session

- Endpoint: `POST /api/workflow/sessions/start`
- Payload: `flowId` plus optional `customerProfileId`
- **Anonymous.** This is the entry point of a public journey, so there is no credential to present
  yet.

The response carries `applicantToken` — a bearer token scoped to the single session it names — plus
`applicantTokenExpiresAt`. Send it as `Authorization: Bearer <token>` on every subsequent request
for that session. It carries no operator rights, so a leaked one exposes the journey its holder was
already completing and nothing else.

An operator who starts a session gets no token back: their own credential already covers it.

### Submit steps

- Endpoint: `POST /api/workflow/sessions/{sessionId}/steps/{nodeId}/submit`
- Payload format: `{ "payload": { ... } }`
- Response: `sessionId`, `isCompleted`, `currentNode` (null once complete), and a **renewed**
  `applicantToken`

Store the renewed token in place of the one you hold. Activity is what extends the credential, so a
long journey never outlives its own token — but only if the caller keeps the renewal.

### Token lifetime and refusal

| Situation | Result |
|-----------|--------|
| Token renewed on each step submission | Journey stays alive indefinitely while being worked on |
| Token expired (`Authentication:ApplicantToken:LifetimeMinutes`, default `SessionTimeoutMinutes`) | `401` |
| Session reached `Completed` or `Abandoned` | Writes refused with `403`; reads still work so a completion screen can render |

A client should treat `401` and `403` on a session it was completing as the same dead end — the
credential is spent and the journey must be restarted — and distinguish both from a `5xx`, which is
retryable.

### Upload documents

- Endpoint: `POST /api/workflow/sessions/{sessionId}/steps/{nodeId}/documents`
- Multipart field name: `files`
- File size limit controlled by `DocumentUpload:MaxFileSizeBytes`
- Accepted types and a maximum file count come from the node's `jsonContent`

### Download documents

- Endpoint: `GET /api/workflow/sessions/{sessionId}/steps/{nodeId}/documents/{fileId}`

Both document endpoints are **scoped to the session in the route**. A token for one session cannot
upload to or read from another. The download additionally scopes the *lookup*: a `fileId` resolves
only when an upload recorded against that session and node carries it, so holding another
applicant's file id does not make it readable through your own session. A file that exists but
belongs elsewhere returns `404`, never `403`.

### Observe progress

- Next step polling endpoint: `GET /api/workflow/sessions/{sessionId}/next`
- SSE stream endpoint: `GET /api/workflow/sessions/{sessionId}/events`
- Session details: `GET /api/workflow/sessions/{sessionId}`

## 5. Validate branching and completion behavior

For each persona, validate:
1. Start node selection is correct.
2. Compliance validation blocks invalid payloads.
3. Conditional transitions route to expected nodes.
4. Document uploads respect type/size constraints.
5. Session reaches expected terminal status (`Completed` or `Abandoned`).

## 6. Configure webhook-based integration tests

Operators can register callbacks:
- `POST /api/flows/{flowId}/webhooks`
- `GET /api/flows/{flowId}/webhooks`
- `DELETE /api/flows/{flowId}/webhooks/{webhookId}`
- `GET /api/flows/{flowId}/webhook-deliveries`

Validation points:
- `session.completed` webhook fires at completion
- Signature header is present: `X-Webhook-Signature`
- Retry behavior is visible in delivery history on transient failure

## 7. Frontend configuration for journey testing

In the frontend `.env.local` file, one value is needed:

- `VITE_API_BASE_URL` — backend host, for example `http://localhost:5072`

**Do not set an API key.** Vite inlines every `VITE_*` variable into the built bundle as a literal
string, and `Authentication:ApiKey` maps to a full `Operator` principal — so a key configured here
ships to every visitor of the public onboarding page. The app needs no credential of its own: the
journey starts anonymously and is handed a token scoped to that session (section 4), and operator
screens use the SSO cookie.

The frontend includes:
- **Visual Journey Builder** (admin UI for creating and editing flows — see section 10)
- **JourneyBuilder** (read-only React Flow graph showing branch-path progress during an active session)
- **StepRenderer** (schema-driven form/doc upload/redirect/info/logic rendering)

Use these components for manual exploratory testing and branch-path verification.

### Routes

| Route | Audience | Credential |
|-------|----------|------------|
| `/` (`/?flowId=<id>` selects a journey) | Applicant | None to start; the per-session token thereafter |
| `/admin` | Operator | SSO cookie |
| `/admin/journey-builder` | Operator | SSO cookie |

Operator surfaces render only behind the SSO check. Nothing on the applicant route reads operator
data, and the applicant page never calls an operator endpoint.

## 8. Inspecting a session as an operator

Open **`/admin` → Sessions → (a session)**. Two complementary views sit side by side:

- **Submissions** — what the applicant entered, per step.
- **Event trail** — how they got there: events in the order they occurred, each with its type, step,
  absolute timestamp, source, payload summary, and the gap since the previous event. The gap is what
  makes a stall visible.

Reading the trail:

- Events are labelled `server` or `client`. Client events are self-reported by the applicant's
  browser and can be **absent entirely** — an ad blocker or a tab closed before the batch flushed
  stops them reaching the API. A trail showing only server events is normal, and the panel says so.
- An empty trail means either that no events were raised or that they aged past
  `Analytics__RetentionDays` and were deleted. After the retention sweep the two are
  indistinguishable, so treat an empty panel on an old session as expected rather than as data loss.

Aggregate drop-off in the flow analytics view is still derived from session status and submissions
rather than from this event stream, so per-step timing is available per session but not across a
flow.

## 9. Rate limits when testing

Limits are **partitioned per caller**, so one client exhausting a budget does not affect another. A
rejected request returns `429` with `Retry-After: 60`.

| Policy | Applies to | Partitioned by | Default (per minute) |
|--------|-----------|----------------|----------------------|
| `session-start` | `POST /api/workflow/sessions/start` | Client IP | 100 |
| `analytics-ingest` | `POST /api/analytics/events` | Applicant session | 120 |
| `webhook-registration` | webhook registration | Authenticated principal | 20 |
| `general` | every other controller endpoint | Authenticated principal, else client IP | 300 |
| global ceiling | every request | not partitioned | 3000 |

Two things to know when load-testing:

- Callers sharing a credential share a partition. A test harness using one API key spends one
  `general` budget across all of it.
- Limits are per-instance, not cluster-wide, and are disabled entirely in the `Testing` environment.

Configure any of them with `RateLimiting__<Key>`. See
[the runbook](./docs/runbook.md#rate-limits) for proxy configuration and multi-replica behaviour.

## 10. Visual Journey Builder (admin UI)

The Visual Journey Builder is a drag-and-drop admin interface for designing and publishing onboarding flows without touching raw JSON.

### Accessing the builder

Navigate to `/admin/journey-builder`. The route is protected by SSO: users must authenticate with the `Operator` role. Unauthenticated requests are redirected to `/login` with a `returnUrl` parameter.

### Canvas interactions

| Action | How |
|--------|-----|
| Move a node | Drag it to the desired position |
| Create a connection | Drag from a node's bottom handle to another node's top handle |
| Select a node or edge | Click it — the Properties Panel opens on the right |
| Delete a selected node or edge | Press `Delete` or `Backspace`, or use the Delete button in the Properties Panel |
| Zoom / pan | Scroll to zoom; drag the background to pan; use the Controls toolbar |

### Adding nodes

Use the **node palette** above the canvas. Each button adds a new node of the corresponding type at a default position:

| Type | Description |
|------|-------------|
| **Form** | Renders a dynamic form from `jsonContent` field definitions |
| **DocumentUpload** | Renders a file picker with type/size constraints |
| **Redirect** | Navigates the customer to an external URL (supports `{{token}}` interpolation) |
| **Information** | Displays a message; no submission required |
| **Logic** | Executes a server-side action automatically (e.g., `SetProfileField`, `HttpCallback`) |

### Properties Panel

Clicking a node opens the Properties Panel with editable fields:

- **Title** — display label shown to the end user
- **Key** — stable slug identifier used in routing and URL interpolation
- **Type** — dropdown selector; changing type does not clear `jsonContent`
- **Start node** — only one node per flow can be the start node; checking this box automatically clears the flag on any other node
- **`jsonContent`** — JSON string carrying type-specific configuration (fields, upload config, redirect URL, action definition)
- **Compliance rules** — optional `complianceRuleJson` for server-side validation at submission time

Clicking an edge opens the edge's condition fields:

- **Condition field**, **operator**, **value** — define the routing predicate evaluated against the step payload
- **Priority** — lower values are evaluated first; leave `conditionField` empty for a fallback (unconditional) edge

### Saving and versioning

| Button | Effect |
|--------|--------|
| **Load flow** | Fetches an existing flow by ID and hydrates the canvas |
| **Create new** | POSTs the current draft to `/api/flows` and assigns the returned ID |
| **Save new version** | PUTs the current draft to `/api/flows/{id}`, incrementing the version counter |
| **Reset** | Clears the canvas back to a single empty start node |

Validation errors (missing flow name, invalid GUIDs, no start node, broken connection references) are shown inline before any save is attempted.
