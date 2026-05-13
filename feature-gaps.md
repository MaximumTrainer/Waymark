# Feature Gaps

This document tracks requirement gaps as user stories with acceptance criteria and validation guidance.

## Gap 1: In-product flow authoring is read-only

### User story
As an operator, I need to create and edit onboarding flows from the frontend UI so non-backend teams can manage journeys without direct API calls.

### Current gap
- Frontend `JourneyBuilder` visualizes a flow but does not persist edits to backend flow APIs.
- Flow CRUD exists in backend (`/api/flows`) but there is no connected authoring UX workflow.

### Acceptance criteria
- Operator can create, edit, version, and delete flows through UI actions.
- UI enforces required node fields and connection validity before save.
- Save operation calls backend APIs and displays deterministic success/failure states.

### Validation
- E2E test: create flow in UI, save, reload, and verify identical graph from `GET /api/flows/{id}`.
- Negative test: invalid connection cannot be saved and user receives validation message.

---

## Gap 2: Persona-driven test harness is not built into the app

### User story
As an SDET, I need a repeatable persona test harness so multiple user profiles can be executed through the same flow and compared automatically.

### Current gap
- API supports `customerProfileId` and rich branching, but there is no bundled runner for persona matrices.
- Validation is mostly manual via API clients and ad hoc scripts.

### Acceptance criteria
- Persona catalog can be defined declaratively (inputs + expected path/status).
- Runner executes sessions for all personas and exports pass/fail report.
- Failures include expected vs actual node transitions.

### Validation
- Automated test suite executes at least one passing and one failing persona scenario.
- CI artifact includes persona execution report.

---

## Gap 3: End-to-end verification coverage is incomplete

### User story
As a team, we need end-to-end coverage across frontend rendering, backend workflow progression, and webhook side effects to reduce release risk.

### Current gap
- Consumer and provider pact tests exist, plus backend application tests.
- There is no full-stack E2E test path covering UI interaction to webhook verification in one scenario.

### Acceptance criteria
- At least one CI E2E pipeline runs journey start, form submission, conditional branch, and completion.
- Webhook emission and signature are validated in the same E2E run.
- Regression failures clearly identify stage (UI, API, or integration callback).

### Validation
- CI run shows deterministic E2E pass with generated artifacts/logs.
- Injected webhook failure scenario verifies retry and delivery log behavior.

---

## Gap 4: Local configuration defaults can cause onboarding startup confusion

### User story
As a new contributor, I need aligned local defaults so first-time setup works without manual troubleshooting.

### Current gap
- `docker-compose.yml` creates database `onboarding`.
- API default connection string in `src/backend/OpenOnboarding.Api/appsettings.json` currently references `open_onboarding` (verified in-repo at the time of this documentation update; not `onboarding`).
- Frontend API helper appends `/api/workflow`; using `VITE_API_BASE_URL` with the same suffix can produce duplicated path segments.

### Acceptance criteria
- Local setup defaults are aligned across compose, backend config, and frontend env examples.
- First run succeeds by following repository docs without additional undocumented overrides.
- Startup diagnostics surface clear configuration errors when misconfigured.

### Validation
- Fresh clone smoke test passes by following Getting Started verbatim.
- Automated check confirms configured base URL resolves valid workflow endpoints.
