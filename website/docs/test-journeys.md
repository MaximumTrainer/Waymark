# Test Journeys

Open-Onboarding ships three reference flows designed for Playwright E2E test verification.  
Each journey isolates a distinct capability of the platform.

---

## Journey A – Linear Basic

**Flow ID:** `a1a1a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a1`  
**Purpose:** Verify the simplest possible linear path: one Form node followed by one Information node.

### Nodes

| Key | Type | Description |
|-----|------|-------------|
| `basic-contact-details` | Form | Collects `FullName` (text, required) and `Email` (email, required) |
| `basic-confirmation` | Information | Confirmation message with a **Continue** button |

### Compliance rules

- `FullName` and `Email` are required fields.
- `Email` must match the pattern `^[^@\s]+@[^@\s]+\.[^@\s]+$`.
- Submitting with missing or invalid fields returns `422 Unprocessable Entity` with a `violations` array.

### Example API walkthrough

```bash
# 1. Start session
curl -X POST http://localhost:5072/api/workflow/sessions/start \
  -H 'X-Api-Key: dev-key' \
  -H 'Content-Type: application/json' \
  -d '{"flowId":"a1a1a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a1"}'

# 2. Submit form (valid)
curl -X POST http://localhost:5072/api/workflow/sessions/{sessionId}/steps/{nodeId}/submit \
  -H 'X-Api-Key: dev-key' \
  -H 'Content-Type: application/json' \
  -d '{"FullName":"Alice Example","Email":"alice@example.com"}'

# 3. Submit Information node (empty payload advances session)
curl -X POST http://localhost:5072/api/workflow/sessions/{sessionId}/steps/{nodeId}/submit \
  -H 'X-Api-Key: dev-key' \
  -H 'Content-Type: application/json' \
  -d '{}'
# → {"isCompleted":true,"currentNode":null}
```

### Schema reference

See [`src/frontend/src/schemas/journey-a-linear-basic.json`](../../src/frontend/src/schemas/journey-a-linear-basic.json)

---

## Journey B – Conditional Branch

**Flow ID:** `b2b2b2b2-b2b2-b2b2-b2b2-b2b2b2b2b2b2`  
**Purpose:** Verify priority-based conditional routing — EU applicants route to a GDPR disclosure, all others to global terms.

### Nodes

| Key | Type | Description |
|-----|------|-------------|
| `country-selection` | Form | Select field: `France`, `Germany`, `USA`, `Other` |
| `eu-gdpr-disclosure` | Information | GDPR rights statement (EU residents) |
| `global-terms-and-conditions` | Information | General terms (non-EU residents) |

### Routing logic

| Country | Next node |
|---------|-----------|
| `France` | `eu-gdpr-disclosure` (Priority 0, `Country == France`) |
| `Germany` | `eu-gdpr-disclosure` (Priority 1, `Country == Germany`) |
| `USA` / `Other` | `global-terms-and-conditions` (Priority 2, fallback — no condition) |

### Example API walkthrough

```bash
# France → EU disclosure
curl -X POST .../steps/{nodeId}/submit -d '{"Country":"France"}'
# → currentNode.key = "eu-gdpr-disclosure"

# USA → global terms
curl -X POST .../steps/{nodeId}/submit -d '{"Country":"USA"}'
# → currentNode.key = "global-terms-and-conditions"
```

### Schema reference

See [`src/frontend/src/schemas/journey-b-conditional-branch.json`](../../src/frontend/src/schemas/journey-b-conditional-branch.json)

---

## Journey C – Compliance Heavy

**Flow ID:** `c3c3c3c3-c3c3-c3c3-c3c3-c3c3c3c3c3c3`  
**Purpose:** Verify strict compliance validation, document upload progression, and server-side URL interpolation for Redirect nodes.

### Nodes

| Key | Type | Description |
|-----|------|-------------|
| `identity-verification` | Form | `NationalId` field with regex pattern `^[A-Z]{2}[0-9]{6}$` |
| `id-document-upload` | DocumentUpload | Accepts `application/pdf` or `image/png`, max 1 file |
| `external-verification` | Redirect | URL contains `{{sessionId}}` → interpolated server-side |

### Compliance rules

- `NationalId` is required.
- `NationalId` must match `^[A-Z]{2}[0-9]{6}$` (e.g. `AB123456`).
- Invalid format returns `422` with `violations[0].field = "NationalId"`.

### URL interpolation

The Redirect node URL template:
```
https://verify.example.com/start?session={{sessionId}}&customer={{externalCustomerId}}
```
The backend substitutes `{{sessionId}}` with the actual session UUID before returning the node to the client.  
The raw `{{sessionId}}` token is **never** sent to the frontend.

### Example API walkthrough

```bash
# 1. Start session
curl -X POST .../sessions/start -d '{"flowId":"c3c3c3c3-..."}'

# 2a. Invalid NationalId → 422
curl -X POST .../steps/{nodeId}/submit -d '{"NationalId":"INVALID"}'
# → 422 {"violations":[{"field":"NationalId","message":"..."}]}

# 2b. Valid NationalId → advances to DocumentUpload
curl -X POST .../steps/{nodeId}/submit -d '{"NationalId":"AB123456"}'
# → currentNode.type = "DocumentUpload"

# 3. DocumentUpload submission (empty payload in test mode skips virus scan)
curl -X POST .../steps/{nodeId}/submit -d '{}'
# → currentNode.type = "Redirect", jsonContent.url contains real sessionId
```

### Schema reference

See [`src/frontend/src/schemas/journey-c-compliance-heavy.json`](../../src/frontend/src/schemas/journey-c-compliance-heavy.json)

---

## Running the Playwright tests

The three API-level journey specs (`journey-a.spec.ts`, `journey-b.spec.ts`, `journey-c.spec.ts`) require a running backend.

```bash
# Start backend (Development environment seeds all flows)
cd src/backend
ASPNETCORE_ENVIRONMENT=Development dotnet run --project OpenOnboarding.Api

# Run Playwright tests (from src/frontend)
cd src/frontend
PLAYWRIGHT_API_BASE_URL=http://localhost:5072 npm run test:e2e
```

The browser-level specs (`onboarding-journeys.spec.ts`, `step-renderer.spec.ts`) mock all API calls and do not require a live backend.

---

## CI integration

The `playwright-e2e` GitHub Actions job:

1. Starts a PostgreSQL service container
2. Builds and starts the backend with `ASPNETCORE_ENVIRONMENT=Development` so DataSeeder seeds all six flows
3. Runs `npm run test:e2e` with `PLAYWRIGHT_API_BASE_URL=http://localhost:5072`
4. Uploads the Playwright HTML report as a build artifact (`playwright-report/`)

See `.github/workflows/ci.yml` → `playwright-e2e` job.
