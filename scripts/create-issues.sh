#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# create-issues.sh
#
# Creates all functional-gap GitHub issues for MaximumTrainer/open-onboarding.
# Requires the GitHub CLI (gh) to be authenticated with `issues: write` scope.
#
# Usage:
#   gh auth login          # authenticate once
#   bash scripts/create-issues.sh
# ---------------------------------------------------------------------------
set -euo pipefail

REPO="MaximumTrainer/open-onboarding"

# ---------------------------------------------------------------------------
# Helper – create a label only if it doesn't already exist
# ---------------------------------------------------------------------------
ensure_label() {
  local name="$1" color="$2" description="$3"
  gh label list --repo "$REPO" --json name --jq '.[].name' 2>/dev/null \
    | grep -qx "$name" \
    || gh label create "$name" --repo "$REPO" --color "$color" --description "$description"
}

echo "==> Ensuring labels exist…"
ensure_label "enhancement"   "a2eeef" "New feature or request"
ensure_label "frontend"      "0075ca" "React / TypeScript / UI work"
ensure_label "backend"       "e4e669" ".NET / API / database work"
ensure_label "documentation" "cfd3d7" "Documentation improvements"
ensure_label "security"      "d73a4a" "Security-related issue"

# ---------------------------------------------------------------------------
# Issue 1 – Schema-driven form rendering
# ---------------------------------------------------------------------------
gh issue create --repo "$REPO" \
  --title "Schema-driven form rendering in StepRenderer" \
  --label "enhancement,frontend" \
  --body "## Background

\`StepRenderer\` currently shows a placeholder comment for \`Form\` nodes instead of dynamically rendering fields from the node's \`jsonContent\` schema. The \`flow-definition.example.json\` already defines a rich field schema (e.g. \`{ \"fields\": [{\"name\": \"Country\", \"type\": \"select\", \"required\": true}] }\`), but the UI ignores it entirely.

This means end-users see a blank card for every form step, making the whole onboarding journey non-functional in the browser.

## Acceptance Criteria

- [ ] \`StepRenderer\` parses \`node.jsonContent\` as JSON when \`node.type === 'Form'\`
- [ ] Supported field types rendered as native HTML controls: \`text\`, \`email\`, \`number\`, \`select\`, \`checkbox\`, \`textarea\`, \`date\`
- [ ] Fields marked \`required: true\` render with the HTML \`required\` attribute and a visible asterisk label
- [ ] Selecting a \`select\` field populates its \`options\` array from \`jsonContent.fields[n].options\`
- [ ] Submitting the form collects all field values into a \`Record<string, unknown>\` keyed by field \`name\` and calls \`onSubmit\`
- [ ] If \`jsonContent\` is missing or unparseable, an error boundary renders a user-friendly message rather than crashing
- [ ] Tailwind / Radix UI component library is used for consistent styling
- [ ] A Storybook story or \`*.test.tsx\` snapshot covers at least: text field, select field, required-field validation feedback

## Happy Path

1. Backend returns a Form node with \`jsonContent = {\"fields\":[{\"name\":\"CompanyName\",\"type\":\"text\",\"required\":true},{\"name\":\"Country\",\"type\":\"select\",\"required\":true,\"options\":[\"USA\",\"UK\",\"Other\"]}]}\`
2. \`StepRenderer\` renders a labelled text input for CompanyName and a dropdown for Country
3. User fills both fields and clicks Submit
4. \`onSubmit({ CompanyName: \"Acme\", Country: \"USA\" })\` is called
5. The workflow advances to the next step

## Edge Cases

- \`jsonContent\` is \`null\` → render an empty form with a Submit button (session can still advance)
- \`jsonContent\` is invalid JSON → display \"This step could not be loaded\" with a retry button; do NOT advance the session
- A field has \`type: \"unknown_custom_type\"\` → fall back to a plain \`<input type=\"text\">\`
- \`options\` array is empty on a \`select\` field → still render the select, showing only the placeholder option
- Form contains 30+ fields → layout must scroll without breaking the step navigation shell
- User submits without filling a required field → HTML5 validation prevents submission and highlights the empty field"

# ---------------------------------------------------------------------------
# Issue 2 – Dynamic JourneyBuilder visualisation
# ---------------------------------------------------------------------------
gh issue create --repo "$REPO" \
  --title "Connect JourneyBuilder to live flow/session data" \
  --label "enhancement,frontend" \
  --body "## Background

\`JourneyBuilder.tsx\` renders a hardcoded three-node React Flow diagram regardless of the actual workflow being executed. This means the visualisation is never accurate and cannot show session progress (e.g. which step the user is on, which branches they have taken, completed vs. pending nodes).

The component needs to source its nodes and edges from the active \`SessionStepResponse\` (and ideally from the full flow definition) instead of a static constant.

## Acceptance Criteria

- [ ] \`JourneyBuilder\` accepts a \`sessionStep: SessionStepResponse | null\` prop (or reads from shared state / context)
- [ ] Nodes are derived from \`flow.nodes\` returned by a new \`GET /api/workflow/flows/{flowId}\` endpoint (see related issue) or encoded in the session response
- [ ] Edges are derived from \`flow.connections\`
- [ ] The node matching \`sessionStep.currentNode.id\` is visually highlighted (distinct colour / ring)
- [ ] Already-visited nodes (nodes with a \`Submission\` in the current session) are marked with a ✓ indicator
- [ ] When \`sessionStep.isCompleted === true\`, all nodes are marked complete and a \"Journey complete\" banner overlays the diagram
- [ ] The visualisation updates in real time as the user progresses through steps without requiring a page reload
- [ ] MiniMap and Controls are retained from the current implementation

## Happy Path

1. User starts a session; \`JourneyBuilder\` fetches and renders all three nodes with the start node highlighted
2. User submits the first form; the Country Form node gains a ✓ and the US SSN Form node becomes highlighted
3. User completes the SSN step; US SSN Form gains a ✓, journey-complete banner appears

## Edge Cases

- Flow has a single node with no connections → diagram renders that one node, completes immediately after submission
- Flow contains a Logic node (non-interactive) → render as a distinct diamond shape
- Flow contains a cycle (unusual but possible via connections) → React Flow renders without infinite loop; guard with a visited-node set
- \`GET /api/workflow/flows/{flowId}\` returns 404 (flow deleted mid-session) → show a skeleton diagram with an error tooltip
- Network request for flow definition fails → fall back to deriving diagram nodes only from the \`currentNode\` in the session response"

# ---------------------------------------------------------------------------
# Issue 3 – Flow CRUD REST API
# ---------------------------------------------------------------------------
gh issue create --repo "$REPO" \
  --title "Flow CRUD REST API (create, read, update, delete flows)" \
  --label "enhancement,backend" \
  --body "## Background

Currently there is no API surface for managing flow definitions. Flows must be inserted directly into the database, making the platform unusable without direct DB access. WiseFlow.ai-style platforms expose full lifecycle management for workflow templates so operators can build and iterate on onboarding journeys without touching infrastructure.

## Acceptance Criteria

- [ ] \`POST /api/flows\` – creates a new flow with nodes and connections; returns \`201 Created\` with the persisted flow including generated IDs
- [ ] \`GET /api/flows\` – returns a paginated list of flows (\`{ items: FlowSummaryDto[], totalCount: int }\`); supports \`?page=\` and \`?pageSize=\` query params
- [ ] \`GET /api/flows/{flowId}\` – returns the full flow including nodes and connections; \`404\` if not found
- [ ] \`PUT /api/flows/{flowId}\` – replaces all nodes and connections atomically (within a transaction); bumps \`version\`; returns \`200\`
- [ ] \`DELETE /api/flows/{flowId}\` – soft-deletes or hard-deletes the flow; returns \`204\`; returns \`409 Conflict\` if active sessions exist
- [ ] \`FlowDto\` contains: \`id, name, description, version, nodes[], connections[]\`
- [ ] \`NodeDto\` (write) validates: \`key\` is non-empty, \`type\` is a valid \`NodeType\`, exactly one node has \`isStartNode: true\`
- [ ] \`ConnectionDto\` (write) validates: \`sourceNodeId\` and \`targetNodeId\` reference nodes within the same flow
- [ ] FluentValidation validators cover all new request types
- [ ] Unit tests cover: create with valid payload, create with duplicate start nodes, get non-existent flow, delete flow with active sessions

## Happy Path

1. Operator posts the JSON from \`flow-definition.example.json\` to \`POST /api/flows\`
2. API persists flow, nodes, and connections in a single transaction; returns \`201\` with IDs
3. Operator fetches \`GET /api/flows/{id}\` and receives the complete definition
4. Operator updates a node title via \`PUT /api/flows/{id}\`; version increments to 2
5. Operator calls \`DELETE /api/flows/{id}\` after migrating users → \`204\`

## Edge Cases

- POST payload has zero nodes → \`400 Bad Request\` with message \"A flow must contain at least one node\"
- POST payload has two nodes with \`isStartNode: true\` → \`400\` with message \"Exactly one start node is required\"
- Connection references a \`targetNodeId\` not present in the same flow → \`400\` with validation detail
- PUT flow where active sessions exist → allow update but record version bump; sessions continue on their original node graph
- Concurrent PUT requests for the same flow → use EF Core optimistic concurrency (\`RowVersion\`) to return \`409\` on conflict
- Flow name is 1000 characters → truncate/reject with max-length validation"

# ---------------------------------------------------------------------------
# Issue 4 – Customer Profile CRUD API
# ---------------------------------------------------------------------------
gh issue create --repo "$REPO" \
  --title "Customer Profile CRUD API endpoints" \
  --label "enhancement,backend" \
  --body "## Background

\`CustomerProfile\` is a first-class domain entity (\`ExternalCustomerId\`, \`Country\`, \`Email\`, \`MetadataJson\`) that is referenced by sessions for conditional branching (e.g. routing by \`Country\`). However, there are no API endpoints to create or manage profiles, so the profile is currently only settable via a nullable \`customerProfileId\` on session start — it must already exist in the database.

## Acceptance Criteria

- [ ] \`POST /api/customers\` – creates a profile; returns \`201\` with the persisted \`CustomerProfileDto\`
- [ ] \`GET /api/customers/{id}\` – returns profile by internal ID; \`404\` if not found
- [ ] \`GET /api/customers?externalId={externalCustomerId}\` – look up by external identifier; returns \`404\` if not found
- [ ] \`PUT /api/customers/{id}\` – updates \`Country\`, \`Email\`, \`MetadataJson\`; returns \`200\`
- [ ] \`DELETE /api/customers/{id}\` – deletes profile; returns \`409\` if the profile has active (non-Completed, non-Abandoned) sessions
- [ ] \`CustomerProfileDto\`: \`id, externalCustomerId, country, email, metadataJson\`
- [ ] FluentValidation: \`email\` must be valid format when present; \`externalCustomerId\` must be non-empty on create
- [ ] \`POST /api/workflow/sessions/start\` should also accept an inline \`customerProfile\` body object (upsert by \`externalCustomerId\`) so callers don't need a separate round-trip

## Happy Path

1. Caller \`POST /api/customers\` with \`{ externalCustomerId: \"cust-001\", country: \"USA\", email: \"alice@example.com\" }\`
2. API returns \`201\` with generated \`id\`
3. Caller uses returned \`id\` in \`POST /api/workflow/sessions/start\`; session inherits Country=USA for conditional branching
4. After onboarding, caller \`PUT /api/customers/{id}\` to update metadata → \`200\`

## Edge Cases

- Create with duplicate \`externalCustomerId\` → \`409 Conflict\`
- \`email\` field is \`\"not-an-email\"\` → \`400\` with FluentValidation error
- \`metadataJson\` is not valid JSON → \`400\` with message \"metadataJson must be valid JSON\"
- Delete customer with completed sessions → allow delete; only block on active sessions
- \`GET /api/customers?externalId=\` (empty string) → \`400 Bad Request\`"

# ---------------------------------------------------------------------------
# Issue 5 – Real file upload handling for DocumentUpload nodes
# ---------------------------------------------------------------------------
gh issue create --repo "$REPO" \
  --title "Implement real file upload handling for DocumentUpload nodes" \
  --label "enhancement,backend,frontend" \
  --body "## Background

\`DocumentUpload\` is a supported \`NodeType\` and the frontend \`StepRenderer\` renders an upload button for it. However, clicking the button immediately submits \`{ uploaded: true }\` without ever collecting an actual file. The backend stores this flag in \`Submission.DataJson\` with no file reference. WiseFlow.ai's platform includes automated document collection and verification — this feature gap makes the DocumentUpload node entirely non-functional.

## Acceptance Criteria

**Backend**
- [ ] \`POST /api/workflow/sessions/{sessionId}/steps/{nodeId}/documents\` accepts a \`multipart/form-data\` request with one or more files
- [ ] Uploaded files are stored to configurable storage: local disk (default dev path) or an abstracted \`IDocumentStorageService\` interface (pluggable for S3, Azure Blob, etc.)
- [ ] Submission \`DataJson\` stores an array of \`{ fileId, fileName, contentType, sizeBytes, storedAt }\` objects
- [ ] \`GET /api/workflow/sessions/{sessionId}/steps/{nodeId}/documents/{fileId}\` streams the file back to the caller
- [ ] \`jsonContent\` field \`acceptedFileTypes\` (array of MIME types) is enforced server-side; unsupported types return \`415 Unsupported Media Type\`
- [ ] Maximum file size is configurable via \`appsettings.json\` (\`DocumentUpload:MaxFileSizeBytes\`); exceeded size returns \`413 Payload Too Large\`
- [ ] Virus/malware scanning hook: \`IDocumentStorageService\` must expose a \`ScanAsync\` method with a default no-op implementation

**Frontend**
- [ ] \`StepRenderer\` renders a native \`<input type=\"file\">\` for DocumentUpload nodes; \`accept\` attribute is set from \`jsonContent.acceptedFileTypes\`
- [ ] Selected files are uploaded to the new multipart endpoint; progress is shown via a progress bar
- [ ] After successful upload, \`onSubmit\` is called with \`{ files: [{ fileId, fileName }] }\`
- [ ] If upload fails, an error message is displayed and the user can retry without restarting the session

## Happy Path

1. Session reaches a DocumentUpload node (Passport Upload)
2. User selects a \`.pdf\` file; frontend POSTs to the documents endpoint with the file
3. Backend stores the file, records the submission, and returns the next step
4. Frontend advances to the next node

## Edge Cases

- User selects an \`.exe\` file → backend rejects with \`415\` even if frontend \`accept\` was bypassed
- File is 0 bytes → reject with \`400\` (\"File must not be empty\")
- User selects 5 files for a node configured for \`maxFiles: 1\` → reject with \`400\`
- Storage disk is full → return \`500\` with a structured error; session remains on the DocumentUpload node
- Network drops mid-upload → frontend shows retry; backend must handle partial/duplicate uploads gracefully (idempotency by content hash)"

# ---------------------------------------------------------------------------
# Issue 6 – Authentication and Authorization
# ---------------------------------------------------------------------------
gh issue create --repo "$REPO" \
  --title "Add authentication and authorization (JWT / API key)" \
  --label "enhancement,backend,security" \
  --body "## Background

All API endpoints are currently unauthenticated. Any caller can start sessions, submit steps, or (once Flow CRUD is added) create/delete flows. This is a critical gap for any real deployment: WiseFlow.ai's platform includes SSO, SAML, and role-based access controls as core enterprise features.

## Acceptance Criteria

- [ ] API supports two authentication modes configurable via \`appsettings.json\`:
  - **JWT Bearer**: validate a signed JWT issued by a configurable OIDC authority
  - **API Key**: validate a static key passed in \`X-Api-Key\` header (suitable for machine-to-machine calls)
- [ ] All existing and future workflow endpoints require authentication by default (\`[Authorize]\`)
- [ ] Two roles are enforced:
  - \`Operator\` – can call Flow CRUD, Customer Profile CRUD, and read any session
  - \`Applicant\` – can only start sessions, submit steps, and read their own session (session ownership checked via \`customerProfileId\`)
- [ ] Unauthenticated requests return \`401 Unauthorized\`
- [ ] Authenticated but unauthorised requests return \`403 Forbidden\`
- [ ] A \`GET /api/auth/me\` endpoint returns the current principal's claims (useful for debugging)
- [ ] Unit tests cover: missing token → 401, valid Operator token → 200, Applicant accessing another user's session → 403
- [ ] README updated with authentication setup instructions

## Happy Path

1. Operator authenticates via OIDC and receives a JWT with \`role: Operator\`
2. Operator creates a flow with JWT in \`Authorization: Bearer\` header → \`201\`
3. Applicant's system calls \`POST /api/workflow/sessions/start\` with \`X-Api-Key\` → \`201\`
4. Applicant submits steps; backend validates the session belongs to the Applicant's customer profile

## Edge Cases

- JWT is expired → \`401\` with \`WWW-Authenticate: Bearer error=\"invalid_token\"\`
- JWT has valid signature but wrong audience → \`401\`
- API key is present but rotated (old key) → \`401\`
- Applicant guesses another session ID → \`403\` (ownership check must use customer profile binding, not just session ID)
- Both JWT and API key sent simultaneously → JWT takes precedence; document the priority order"

# ---------------------------------------------------------------------------
# Issue 7 – Extended condition operators
# ---------------------------------------------------------------------------
gh issue create --repo "$REPO" \
  --title "Add extended condition operators for workflow branching (Contains, StartsWith, GreaterThan, Regex, etc.)" \
  --label "enhancement,backend" \
  --body "## Background

The \`ConditionOperator\` enum only supports \`Equals\`, \`NotEquals\`, and \`Exists\`. Real compliance workflows need numeric comparisons (e.g. branch if annual revenue > £1M), substring matching (e.g. email domain), and pattern matching (e.g. postcode format). Expanding the operator set allows flow designers to express richer branching without adding custom Logic nodes.

## Acceptance Criteria

- [ ] \`ConditionOperator\` enum extended with: \`Contains\`, \`NotContains\`, \`StartsWith\`, \`EndsWith\`, \`GreaterThan\`, \`LessThan\`, \`GreaterThanOrEqual\`, \`LessThanOrEqual\`, \`MatchesRegex\`
- [ ] \`WorkflowService.EvaluateCondition\` implements each new operator:
  - String operators (\`Contains\`, \`StartsWith\`, \`EndsWith\`) are case-insensitive
  - Numeric operators (\`GreaterThan\` etc.) attempt \`decimal.TryParse\` on both sides; if either side is non-numeric the condition returns \`false\`
  - \`MatchesRegex\` compiles the \`ConditionValue\` as a .NET regex with a timeout of 100 ms (prevent ReDoS)
- [ ] EF Core migration adds the new enum values without breaking existing rows
- [ ] FluentValidation for Connection DTOs rejects unrecognised operator strings
- [ ] Unit tests cover every new operator with a passing and a failing example
- [ ] \`flow-definition.example.json\` updated with at least one \`Contains\` or \`GreaterThan\` example

## Happy Path

1. Flow designer creates a connection with \`conditionOperator: \"GreaterThan\", conditionField: \"AnnualRevenue\", conditionValue: \"1000000\"\`
2. Applicant submits \`{ AnnualRevenue: \"2500000\" }\`
3. Condition evaluates \`2500000 > 1000000 = true\`; session routes to the high-value KYC node

## Edge Cases

- \`MatchesRegex\` with catastrophic backtracking pattern (e.g. \`(a+)+\`) → enforced 100 ms timeout returns \`false\` and logs a warning
- Numeric comparison where field value is \`\"N/A\"\` (non-numeric) → condition returns \`false\` gracefully; session falls through to next priority connection
- \`Contains\` with empty \`conditionValue\` → always returns \`true\` (every string contains the empty string); document this behaviour"

# ---------------------------------------------------------------------------
# Issue 8 – Submission retrieval & analytics endpoints
# ---------------------------------------------------------------------------
gh issue create --repo "$REPO" \
  --title "Submission retrieval and session analytics endpoints" \
  --label "enhancement,backend" \
  --body "## Background

Once sessions are completed, there is no API to retrieve the collected data. Operators have no way to read submissions, audit a session's history, or aggregate completion statistics — all of which are core to WiseFlow.ai's compliance dashboard and audit-trail feature.

## Acceptance Criteria

- [ ] \`GET /api/workflow/sessions/{sessionId}/submissions\` – returns all submissions for a session in chronological order as \`SubmissionDto[]\`
- [ ] \`SubmissionDto\`: \`{ id, sessionId, nodeId, nodeKey, submittedAt, dataJson }\`
- [ ] \`GET /api/workflow/sessions/{sessionId}\` – returns full session detail including \`status\`, \`createdAt\`, \`updatedAt\`, \`customerProfileId\`, and \`currentNode\`
- [ ] \`GET /api/workflow/sessions?flowId={id}&status={status}&page=&pageSize=\` – paginated list of sessions for an operator dashboard
- [ ] \`GET /api/workflow/flows/{flowId}/stats\` – returns \`{ totalSessions, completedSessions, abandonedSessions, averageCompletionTimeSeconds, dropOffByNodeKey }\`
- [ ] All retrieval endpoints require \`Operator\` role (once auth is added); Applicants may only access their own session
- [ ] Unit tests: fetch submissions for completed session, fetch sessions by flow + status filter

## Happy Path

1. Operator queries \`GET /api/workflow/sessions?flowId=…&status=Completed&page=1&pageSize=20\`
2. Receives list of 20 completed sessions with pagination metadata
3. Clicks into one session → \`GET /api/workflow/sessions/{id}/submissions\` shows all collected form data
4. Calls \`GET /api/workflow/flows/{flowId}/stats\` to see that 40% of users drop off at the DocumentUpload step

## Edge Cases

- Session has no submissions (just started) → returns empty array \`[]\`, not \`404\`
- \`dataJson\` contains PII → consider a \`?maskPii=true\` query param that redacts values for fields marked \`piiField: true\` in the node schema
- Stats endpoint called for a flow with zero sessions → returns zeroed-out stats object, not \`404\`
- \`pageSize\` query param exceeds 100 → cap at 100 and include a warning in the response"

# ---------------------------------------------------------------------------
# Issue 9 – Session abandonment endpoint
# ---------------------------------------------------------------------------
gh issue create --repo "$REPO" \
  --title "Add explicit session abandonment endpoint" \
  --label "enhancement,backend" \
  --body "## Background

\`SessionStatus\` includes an \`Abandoned\` state but there is no API endpoint to transition a session to that state. Currently sessions are either Completed by the state machine or remain Started indefinitely. Operators need to be able to abandon stale sessions (e.g. after a timeout), and applicants should be able to explicitly cancel their own journey.

## Acceptance Criteria

- [ ] \`DELETE /api/workflow/sessions/{sessionId}\` (or \`POST …/abandon\`) transitions the session to \`Abandoned\` and sets \`UpdatedAt\`
- [ ] Abandoning an already-Completed session returns \`409 Conflict\` with message \`\"Session is already completed\"\`
- [ ] Abandoning an already-Abandoned session is idempotent → returns \`200\` or \`204\`
- [ ] Response body includes the final \`SessionStepResponse\` with \`isCompleted: false\` and \`currentNode: null\` to signal termination
- [ ] A background job or \`IHostedService\` automatically abandons sessions that have been in \`Started\` status for longer than a configurable \`SessionTimeoutMinutes\` (default: 1440 / 24 h)
- [ ] Unit tests: abandon started session, attempt to abandon completed session → 409, auto-abandonment via hosted service

## Happy Path

1. Applicant calls the abandon endpoint mid-journey
2. Session transitions to Abandoned; API returns success response
3. Applicant calls \`GET /api/workflow/sessions/{id}/next\` → returns \`{ isCompleted: true, currentNode: null }\` indicating closure
4. Auto-abandonment job runs nightly and marks sessions idle for > 24 h as Abandoned

## Edge Cases

- Session is currently being submitted (race condition between submit and abandon) → use an EF Core row-level lock or optimistic concurrency; last-writer-wins with appropriate error
- Abandon request sent twice concurrently → idempotent; only one DB write occurs
- \`SessionTimeoutMinutes\` set to 0 → treat as disabled; document this"

# ---------------------------------------------------------------------------
# Issue 10 – EF Core migrations and seed data
# ---------------------------------------------------------------------------
gh issue create --repo "$REPO" \
  --title "Add EF Core database migrations and example seed data" \
  --label "enhancement,backend" \
  --body "## Background

There are no EF Core migrations in the repository. To run the application, a developer must manually create the database schema. There is also no seed data, so after a fresh setup the API has no flows to start a session from. This blocks contributor onboarding and makes CI/CD pipelines brittle.

## Acceptance Criteria

- [ ] Initial EF Core migration created (\`dotnet ef migrations add InitialCreate\`) and committed under \`src/backend/OpenOnboarding.Infrastructure/Migrations/\`
- [ ] Migration is applied automatically on app startup in Development environment via \`dbContext.Database.MigrateAsync()\` (or a startup filter)
- [ ] A \`DataSeeder\` class seeds the example flow from \`flow-definition.example.json\` in Development environment only, skipping if a flow with the same ID already exists
- [ ] A \`docker-compose.yml\` at the repo root starts PostgreSQL with the correct credentials and applies migrations
- [ ] README updated with: \`docker-compose up -d\`, \`dotnet run\` (backend), \`npm run dev\` (frontend) quick-start
- [ ] CI workflow (GitHub Actions) updated to spin up PostgreSQL service and run \`dotnet ef database update\` before running tests

## Happy Path

1. Contributor clones repo
2. Runs \`docker-compose up -d\` → Postgres starts
3. Runs \`dotnet run --project src/backend/OpenOnboarding.Api\` → migrations applied, seed data inserted
4. Frontend starts and immediately hits a live session for the example compliance flow

## Edge Cases

- Migration is applied against a DB that already has some tables (partial schema) → EF Core \`__EFMigrationsHistory\` table prevents re-running applied migrations
- Seed data insertion fails because the example flow ID already exists → upsert / skip silently
- Production environment: \`DataSeeder\` must NOT run; guard with \`IWebHostEnvironment.IsDevelopment()\`"

# ---------------------------------------------------------------------------
# Issue 11 – OpenAPI / Swagger UI documentation
# ---------------------------------------------------------------------------
gh issue create --repo "$REPO" \
  --title "Configure OpenAPI / Swagger UI documentation" \
  --label "enhancement,backend,documentation" \
  --body "## Background

\`Program.cs\` calls \`builder.Services.AddEndpointsApiExplorer()\` and \`builder.Services.AddOpenApi()\`, but there is no Swagger UI middleware configured and no XML documentation attributes on controller actions. API consumers have no discoverable documentation.

## Acceptance Criteria

- [ ] Swagger UI available at \`/swagger\` in Development environment
- [ ] All controller actions annotated with \`[ProducesResponseType]\` for each possible HTTP status code
- [ ] Request/response DTOs annotated with XML doc comments (\`<summary>\`, \`<param>\`, \`<returns>\`) and \`[Required]\` / \`[JsonPropertyName]\` attributes
- [ ] \`operationId\` set for every operation to enable clean client SDK generation
- [ ] OpenAPI JSON exported to \`docs/openapi.json\` via a \`dotnet run --project … --generate-openapi\` script or CI step
- [ ] Info block includes: title \`Open Onboarding API\`, version (\`v1\`), contact email, license (MIT), description linking to README

## Happy Path

1. Developer runs backend locally
2. Navigates to \`http://localhost:5072/swagger\`
3. Sees all three (plus future) endpoints with full request/response schemas
4. Clicks \"Try it out\" → POST to \`/sessions/start\` with a valid \`flowId\` → gets back session response

## Edge Cases

- Swagger UI must be disabled in Production to avoid leaking schema info (guard with \`app.Environment.IsDevelopment()\`)
- Enum values (\`NodeType\`, \`SessionStatus\`, \`ConditionOperator\`) must render as their string names, not integers, in the Swagger schema
- Nullable fields must show \`nullable: true\` in the schema"

# ---------------------------------------------------------------------------
# Issue 12 – Real-time notifications via SSE / SignalR
# ---------------------------------------------------------------------------
gh issue create --repo "$REPO" \
  --title "Implement real-time session progress notifications (SSE or SignalR)" \
  --label "enhancement,backend,frontend" \
  --body "## Background

WiseFlow.ai's platform provides real-time notifications to keep all stakeholders informed of onboarding progress or required actions. Currently, the only way to check session status is polling \`GET /sessions/{id}/next\`. There is no push mechanism to notify operators or third-party integrations when a session advances, completes, or is abandoned.

## Acceptance Criteria

**Backend (choose one: SSE or SignalR Hub)**
- [ ] \`GET /api/workflow/sessions/{sessionId}/events\` streams Server-Sent Events with event types: \`step-advanced\`, \`session-completed\`, \`session-abandoned\`
- [ ] Each event payload contains a \`SessionStepResponse\`-compatible JSON object
- [ ] Events are emitted by \`WorkflowService\` via an \`ISessionEventEmitter\` abstraction (enables testing without real HTTP streams)
- [ ] Connections are cleaned up when the session reaches a terminal state or the client disconnects

**Frontend**
- [ ] \`useOnboarding\` hook establishes an \`EventSource\` connection after session start
- [ ] Incoming events update the hook's \`step\` state, replacing the need to call \`getNextStep\` after submission
- [ ] Hook closes the \`EventSource\` on session completion or component unmount
- [ ] Falls back to polling (\`GET /sessions/{id}/next\` every 5 s) if \`EventSource\` is not supported

**Webhook (operator notifications)**
- [ ] \`POST /api/flows/{flowId}/webhooks\` registers a URL to receive \`session-completed\` events as HTTP POST
- [ ] Webhook payload includes session ID, customer profile ID, and completion timestamp
- [ ] Delivery includes retry logic (3 attempts, exponential backoff) and a signature header (\`X-Webhook-Signature\`) using HMAC-SHA256

## Happy Path

1. Operator registers a webhook on their flow
2. Applicant completes the onboarding journey
3. Backend emits \`session-completed\` event; SSE client (JourneyBuilder) updates instantly
4. Operator's webhook endpoint receives a signed POST within 2 s

## Edge Cases

- Client disconnects mid-journey → SSE connection dropped; when client reconnects, it should receive the latest \`step-advanced\` event via \`Last-Event-ID\` support
- Webhook URL returns \`500\` → retry 3× with backoff; after exhaustion, mark delivery as failed and surface in a \`GET /api/flows/{flowId}/webhook-deliveries\` log
- Multiple browser tabs open for the same session → each receives the same events independently"

# ---------------------------------------------------------------------------
# Issue 13 – Redirect node dynamic URL parameter interpolation
# ---------------------------------------------------------------------------
gh issue create --repo "$REPO" \
  --title "Redirect node: interpolate session and submission data into redirect URL" \
  --label "enhancement,backend,frontend" \
  --body "## Background

The \`Redirect\` node type is rendered by \`StepRenderer\` as a plain anchor tag using the raw URL from \`jsonContent\`. It does not inject any session context (e.g. session ID, customer external ID, submission values) into the URL. This limits Redirect nodes to static destinations and prevents integration with external identity-verification or e-signature platforms that require a return URL or user reference in the query string.

## Acceptance Criteria

- [ ] \`jsonContent\` for Redirect nodes supports a \`url\` template string with \`{{variable}}\` placeholders, e.g. \`https://verify.example.com?session={{sessionId}}&email={{email}}\`
- [ ] Available interpolation variables: \`sessionId\`, \`flowId\`, \`nodeKey\`, \`customerProfileId\`, \`externalCustomerId\`, and any field name from the most recent submission in the same session
- [ ] Interpolation is performed server-side in \`WorkflowService.GetNextStepAsync\` / \`StartSessionAsync\`; the resolved URL is returned in \`NodeDto.jsonContent\`
- [ ] If a placeholder references an unknown variable, the placeholder is removed and a warning is logged (do not leak unresolved \`{{…}}\` to the client)
- [ ] Client-side: \`StepRenderer\` renders a styled button/link using the resolved URL; \`getSafeRedirectUrl\` protocol check is retained
- [ ] Unit tests: template with known variables → correct URL, template with unknown variable → placeholder stripped

## Happy Path

1. Flow includes a Redirect node with \`url: \"https://kyc.example.com/start?ref={{externalCustomerId}}&return={{sessionId}}\"\`
2. Applicant (externalCustomerId: \`cust-001\`) reaches this step in session \`abc-123\`
3. \`NodeDto.jsonContent\` returned to frontend has resolved URL: \`https://kyc.example.com/start?ref=cust-001&return=abc-123\`
4. Frontend renders a \"Continue to Verification\" button linking to the resolved URL

## Edge Cases

- URL template is not valid JSON (raw string) → treat the entire \`jsonContent\` as a plain URL (backward-compatible)
- Resolved URL does not start with \`https://\` → \`getSafeRedirectUrl\` returns \`\"#\"\` and logs a security warning
- Placeholder value contains special characters → URL-encode the interpolated value to prevent injection
- Two submissions in the same session have the same field name with different values → use the most recent submission's value"

# ---------------------------------------------------------------------------
# Issue 14 – Logic node execution engine
# ---------------------------------------------------------------------------
gh issue create --repo "$REPO" \
  --title "Logic node server-side execution engine" \
  --label "enhancement,backend" \
  --body "## Background

\`NodeType.Logic\` exists in the domain and is rendered in \`StepRenderer\` by simply displaying the node key. Logic nodes are intended to perform automated server-side processing (data transformation, external API calls, compliance score calculation) without requiring user interaction. Currently they are completely non-functional — the state machine treats them identically to Form nodes, blocking the session until the user submits.

## Acceptance Criteria

- [ ] Logic nodes are automatically processed by the state machine without requiring a client submission: when the resolved next node is a Logic node, \`SubmitStepAsync\` (or a new \`ProcessLogicNodeAsync\` method) immediately executes the node and advances to the following node
- [ ] \`jsonContent\` for Logic nodes supports an \`action\` field with at least two built-in actions:
  - \`\"SetProfileField\"\` – writes a value to \`CustomerProfile.MetadataJson\` (e.g. set \`risk_tier = \"high\"\`)
  - \`\"HttpCallback\"\` – POSTs current submission data to a configured URL and stores the response body as a submission
- [ ] Logic node execution errors are caught and stored in a new \`ExecutionErrorJson\` column on \`Node\`; the session continues to the next node (non-blocking by default) unless \`jsonContent.failOnError: true\`
- [ ] A \`ILogicNodeExecutor\` interface is defined to make actions pluggable; built-in actions registered via DI
- [ ] Frontend: when \`StepRenderer\` receives a Logic node, it renders a spinner/progress indicator rather than a form, then immediately calls \`getNextStep\`
- [ ] Unit tests: SetProfileField action updates customer profile, HttpCallback action with mock HTTP client, failOnError=true causes session to halt

## Happy Path

1. Session reaches a Logic node with \`action: \"SetProfileField\", field: \"kyc_status\", value: \"pending\"\`
2. State machine executes the action instantly, updates the customer profile, and advances to the next node
3. Frontend receives the post-logic node in the \`SessionStepResponse\` without showing an interactive form

## Edge Cases

- Logic node is the terminal node (no outgoing connections) → session completes after execution
- \`HttpCallback\` URL is unreachable → if \`failOnError: false\`, log error and continue; if \`failOnError: true\`, session status set to a new \`Error\` state
- Infinite loop: Logic → Logic → Logic with circular connections → detect cycle (max 20 auto-advances) and halt with error
- \`SetProfileField\` attempts to overwrite a PII field → allow but emit an audit log entry"

# ---------------------------------------------------------------------------
# Issue 15 – Expanded compliance rule validators
# ---------------------------------------------------------------------------
gh issue create --repo "$REPO" \
  --title "Expand compliance rule JSON schema beyond requiredFields" \
  --label "enhancement,backend" \
  --body "## Background

\`ComplianceRuleJson\` on \`Node\` only supports \`{ \"requiredFields\": [\"Field1\", \"Field2\"] }\`. WiseFlow.ai's compliance engine enforces richer rules: field-level format validation, cross-field rules, numeric range checks, and custom regex patterns. The current validator will reject any \`ComplianceRuleJson\` structure outside the single-key pattern, making it impossible to express meaningful compliance constraints.

## Acceptance Criteria

- [ ] \`ComplianceRuleJson\` schema extended to support the following rule types (backward-compatible — existing \`requiredFields\` arrays continue to work):
  - \`minLength\` / \`maxLength\`: string field length bounds
  - \`pattern\`: ECMAScript-compatible regex applied to a string field value
  - \`minimum\` / \`maximum\`: numeric bounds parsed as \`decimal\`
  - \`allowedValues\`: enumeration of permitted values (replaces repeated Equals conditions for simple dropdowns)
  - \`crossFieldRule\`: two fields and a comparison operator (e.g. \`EndDate > StartDate\`)
- [ ] A \`ComplianceRuleEvaluator\` service validates a submission \`payload\` against a node's rules and returns a structured \`ComplianceViolation[]\` (field name + message)
- [ ] \`WorkflowService.SubmitStepAsync\` uses \`ComplianceRuleEvaluator\` and throws \`ValidationException\` with all violations (not just the first)
- [ ] FluentValidation on the \`Node\` write DTO validates that \`ComplianceRuleJson\` is valid JSON conforming to the extended schema
- [ ] Unit tests: required field missing, regex mismatch, numeric out of range, cross-field violation, all rules passing simultaneously

## Happy Path

1. Node has \`complianceRuleJson: { \"requiredFields\": [\"Ssn\"], \"rules\": [{ \"field\": \"Ssn\", \"pattern\": \"^[0-9]{3}-[0-9]{2}-[0-9]{4}$\" }] }\`
2. User submits \`{ Ssn: \"123-45-6789\" }\` → passes
3. User submits \`{ Ssn: \"ABCDE\" }\` → \`ValidationException\` with message \`\"Ssn does not match required pattern\"\`

## Edge Cases

- \`pattern\` is an invalid regex → \`400\` when saving the node (caught in FluentValidation before data is persisted)
- \`crossFieldRule\` references a field that is not in the current submission (e.g. set in an earlier step) → look up value from previous submissions in the session; if not found, treat as \`null\` and evaluate accordingly
- Multiple rules fail simultaneously → all violations returned in one \`ValidationException\`, not just the first"

# ---------------------------------------------------------------------------
# Issue 16 – Role-based access control
# ---------------------------------------------------------------------------
gh issue create --repo "$REPO" \
  --title "Implement role-based access control (RBAC) — Operator vs. Applicant" \
  --label "enhancement,backend,security" \
  --body "## Background

This issue depends on Issue #6 (Authentication). Once authentication is in place, the API needs role enforcement. Without RBAC, any authenticated applicant can list all sessions, modify flows, or delete customer profiles — a significant security and compliance risk for a multi-tenant onboarding platform.

## Acceptance Criteria

- [ ] \`Operator\` role: full access to Flow CRUD, Customer Profile CRUD, session listing for any customer, flow stats, webhook management
- [ ] \`Applicant\` role:
  - Can call \`POST /sessions/start\` (creates a session bound to their identity)
  - Can call \`POST /sessions/{id}/steps/{nodeId}/submit\` only for sessions they own
  - Can call \`GET /sessions/{id}/next\` only for sessions they own
  - Cannot access Flow CRUD, other customers' sessions, or analytics endpoints
- [ ] Session ownership is determined by: \`Session.CustomerProfileId == currentUser.CustomerProfileId\` (resolved from JWT claim or API key mapping)
- [ ] Ownership violation returns \`403 Forbidden\`, not \`404\` (to avoid leaking session existence via side-channel)
- [ ] A \`ReadOnly\` role (optional, for auditors) can read sessions and submissions but cannot submit or modify anything
- [ ] Policy-based authorization (\`IAuthorizationHandler\`) used rather than inline role checks in controllers
- [ ] Integration tests: Applicant A cannot access Applicant B's session, Operator can access any session, ReadOnly cannot submit

## Happy Path

1. Operator creates a flow and shares the flow ID with applicants
2. Applicant authenticates and starts a session; session is bound to their profile
3. Applicant submits steps; each request is validated for ownership
4. Operator views all completed sessions for the flow via the analytics endpoint

## Edge Cases

- Applicant tries to submit to a session started by another applicant → \`403\` (not \`404\`)
- JWT claims contain both \`Operator\` and \`Applicant\` roles → \`Operator\` wins (most permissive)
- Flow is deleted by Operator while Applicant's session is in progress → Applicant can still complete their session (session holds a restrict-delete reference to the flow)
- Service account (machine-to-machine with API key) needs Operator access → map API key to a virtual Operator principal in the authentication handler"

echo ""
echo "==> All 16 issues created successfully!"
