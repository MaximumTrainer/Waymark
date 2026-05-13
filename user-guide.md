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

Connection conditions use operators from `ConditionOperator` (for example: `Equals`, `NotEquals`, `GreaterThan`, `MatchesRegex`).

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

In `src/frontend/.env.local`:
- `VITE_API_BASE_URL` should point to backend host (for example `http://localhost:5072`)
- `VITE_API_KEY` should match backend API key for local operator testing

The frontend includes:
- JourneyBuilder (visual branch path)
- StepRenderer (schema-driven form/doc upload/redirect/info/logic rendering)

Use these components for manual exploratory testing and branch-path verification.
