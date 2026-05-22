# User Guide

This guide explains how to configure and test onboarding journeys for different user personas.

## 1. Persona model

The API uses role-based access control:
- `Operator`: manage flows, customers, sessions, and webhooks
- `Applicant`: run onboarding sessions, submit steps, and access owned sessions
- `ReadOnly`: read-only access to operator endpoints where permitted by policy

In local development, `X-Api-Key` authentication maps requests to operator behavior.

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

### Submit steps

- Endpoint: `POST /api/workflow/sessions/{sessionId}/steps/{nodeId}/submit`
- Payload format: `{ "payload": { ... } }`

### Upload documents

- Endpoint: `POST /api/workflow/sessions/{sessionId}/steps/{nodeId}/documents`
- Multipart field name: `files`
- File size limit controlled by `DocumentUpload:MaxFileSizeBytes`

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

In the frontend `.env.local` file:
- `VITE_API_BASE_URL` should point to backend host (for example `http://localhost:5072`)
- `VITE_API_KEY` should match backend API key for local operator testing

The frontend includes:
- **Visual Journey Builder** (admin UI for creating and editing flows — see section 8)
- **JourneyBuilder** (read-only React Flow graph showing branch-path progress during an active session)
- **StepRenderer** (schema-driven form/doc upload/redirect/info/logic rendering)

Use these components for manual exploratory testing and branch-path verification.

## 8. Visual Journey Builder (admin UI)

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
