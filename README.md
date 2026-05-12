# open-onboarding

Schema-driven onboarding boilerplate for automated user journeys and compliance workflows. Define a directed graph of steps in JSON, start a session, and let the engine guide applicants through conditional logic, file uploads, and compliance checks — all with real-time SSE progress events and webhook notifications.

## Features

- **Schema-driven flows** — define nodes (Form, Info, Logic, DocumentUpload, Redirect) and connections with conditional rules in JSON
- **Compliance rule engine** — field-level and cross-field validation rules evaluated server-side before advancing
- **Logic node execution** — automatic `SetProfileField` and `HttpCallback` actions with cycle detection
- **Redirect nodes** — server-side `{{variable}}` URL interpolation from session data
- **File upload** — multipart document upload per step with configurable size limit
- **Real-time SSE** — server-sent events stream for live session progress; frontend falls back to polling
- **Webhooks** — HMAC-SHA256-signed `session.completed` events with exponential-backoff retry
- **JWT + API key auth** — JWT Bearer for production; `X-Api-Key` header for dev/service use
- **Role-based access control** — `Operator`, `Applicant`, `ReadOnly` roles with resource-level session ownership
- **JourneyBuilder UI** — React Flow canvas showing live flow layout with visited/active node highlights
- **Schema-driven forms** — `StepRenderer` renders all field types from `jsonContent` with validation

---

## Quick start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 22+](https://nodejs.org/)
- [Docker](https://www.docker.com/) (for PostgreSQL)

### 1. Start PostgreSQL

```bash
docker-compose up -d
```

### 2. Start the backend

Migrations and seed data are applied automatically on first run in the `Development` environment.

```bash
dotnet run --project src/backend/OpenOnboarding.Api
# API:     https://localhost:7000
# HTTP:    http://localhost:5072
# Swagger: https://localhost:7000/swagger
```

### 3. Start the frontend

```bash
cd src/frontend
npm install
npm run dev
# UI: http://localhost:5173
```

The Vite dev server proxies `/api` → `http://localhost:5072` automatically.

---

## Architecture

The backend follows a **Ports & Adapters (Hexagonal)** pattern with four layers:

```
┌────────────────────────────────────────────────┐
│  OpenOnboarding.Api          (controllers, auth) │
├────────────────────────────────────────────────┤
│  OpenOnboarding.Application  (ports/interfaces, │
│                               DTOs, validators)  │
├────────────────────────────────────────────────┤
│  OpenOnboarding.Infrastructure  (EF Core,        │
│   WorkflowService, adapters)                     │
├────────────────────────────────────────────────┤
│  OpenOnboarding.Domain      (entities, enums)   │
└────────────────────────────────────────────────┘
```

- **Domain** — pure entities (`Flow`, `Node`, `Connection`, `Session`, `Submission`, `CustomerProfile`, `Webhook`). No framework dependencies.
- **Application** — ports (`IWorkflowService`, `IFlowService`, `ICustomerService`, `IComplianceRuleEvaluator`, `IDocumentStorageService`, `ISessionEventEmitter`, `IWebhookService`, …), request/response DTOs, FluentValidation validators.
- **Infrastructure** — EF Core `OnboardingDbContext`, `WorkflowService` state machine, compliance evaluator, logic node executors, local document storage, in-memory SSE channels, webhook delivery.
- **Api** — thin ASP.NET Core controllers, combined JWT + API key auth scheme, RBAC policy handlers, Swagger/OpenAPI.

---

## Directory layout

```
open-onboarding/
├── docker-compose.yml
├── Dockerfile                          # multi-stage .NET 10 build
├── src/
│   ├── backend/
│   │   ├── OpenOnboarding.Domain/
│   │   │   ├── Entities/               # Flow, Node, Connection, Session, …
│   │   │   └── Enums/                  # NodeType, SessionStatus, ConditionOperator
│   │   ├── OpenOnboarding.Application/
│   │   │   ├── Contracts/              # DTOs and request/response types
│   │   │   ├── Interfaces/             # service ports
│   │   │   └── Validators/             # FluentValidation
│   │   ├── OpenOnboarding.Infrastructure/
│   │   │   ├── Migrations/             # EF Core migrations
│   │   │   ├── Persistence/            # OnboardingDbContext, DataSeeder
│   │   │   ├── Services/               # WorkflowService, ComplianceRuleEvaluator, …
│   │   │   └── DependencyInjection/    # ServiceCollectionExtensions
│   │   ├── OpenOnboarding.Api/
│   │   │   ├── Controllers/            # FlowsController, WorkflowController, …
│   │   │   ├── Authentication/         # ApiKeyAuthenticationHandler
│   │   │   ├── Authorization/          # AppRoles, SessionOwnershipHandler
│   │   │   └── Program.cs
│   │   └── OpenOnboarding.Application.Tests/
│   └── frontend/
│       └── src/
│           ├── onboarding/
│           │   ├── components/         # StepRenderer, JourneyBuilder, FieldInput
│           │   └── hooks/              # useOnboarding (SSE + polling)
│           └── schemas/
│               └── flow-definition.example.json
└── .github/workflows/
    ├── ci.yml                          # build + test on PR / push to main
    ├── cd-azure.yml                    # deploy to Azure Container Apps + Static Web Apps
    └── cd-aws.yml                      # deploy to AWS ECS + S3 + CloudFront
```

---

## API reference

All endpoints require an `Authorization` header (see [Authentication](#authentication)).

### Flows — `api/flows` (Operator only)

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/flows` | Create a flow with nodes and connections |
| `GET` | `/api/flows?page=1&pageSize=20` | Paginated list of flows |
| `GET` | `/api/flows/{flowId}` | Full flow with nodes and connections |
| `PUT` | `/api/flows/{flowId}` | Replace a flow definition |
| `DELETE` | `/api/flows/{flowId}` | Delete a flow |
| `GET` | `/api/flows/{flowId}/stats` | Aggregate analytics (total, completed, abandoned, avg duration) |

### Workflow sessions — `api/workflow`

| Method | Path | Roles | Description |
|--------|------|-------|-------------|
| `POST` | `/api/workflow/sessions/start` | Applicant, Operator | Start a new session |
| `GET` | `/api/workflow/sessions/{id}` | Applicant†, Operator | Get session detail |
| `GET` | `/api/workflow/sessions/{id}/next` | Applicant†, Operator | Get next pending step |
| `POST` | `/api/workflow/sessions/{id}/steps/{nodeId}/submit` | Applicant†, Operator | Submit step data (runs compliance, advances flow) |
| `POST` | `/api/workflow/sessions/{id}/steps/{nodeId}/documents` | Applicant†, Operator | Multipart file upload for DocumentUpload nodes |
| `DELETE` | `/api/workflow/sessions/{id}` | Applicant†, Operator | Abandon an active session |
| `GET` | `/api/workflow/sessions` | Operator | Paginated session list (filter by `flowId`, `status`) |
| `GET` | `/api/workflow/sessions/{id}/submissions` | Operator | All submissions for a session |
| `GET` | `/api/workflow/events/{id}` | Operator | SSE stream for real-time session events |

† Applicants can only access sessions they own (matched by `customerProfileId` claim).

### Customers — `api/customers` (Operator only)

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/customers` | Create a customer profile |
| `GET` | `/api/customers/{id}` | Get by internal ID |
| `GET` | `/api/customers?externalId=...` | Get by external customer ID |
| `PUT` | `/api/customers/{id}` | Update a customer profile |
| `DELETE` | `/api/customers/{id}` | Delete a customer profile |

### Webhooks — `api/flows/{flowId}/webhooks` (Operator only)

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/flows/{flowId}/webhooks` | Register a webhook URL |
| `GET` | `/api/flows/{flowId}/webhooks` | List webhooks for a flow |
| `DELETE` | `/api/flows/{flowId}/webhooks/{webhookId}` | Remove a webhook |
| `GET` | `/api/flows/{flowId}/webhooks/{webhookId}/deliveries` | Delivery history |

### Auth

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/auth/me` | Returns claims for the current authenticated principal |

---

## Authentication

The API supports two authentication methods, configured in `appsettings.json`:

### API key (development / service-to-service)

Add the header `X-Api-Key: <value>` where value matches `Authentication:ApiKey`. API key callers are automatically granted the `Operator` role.

```bash
curl -H "X-Api-Key: dev-api-key-change-in-production" https://localhost:7000/api/flows
```

### JWT Bearer (production)

Set `Authentication:JwtAuthority` to your OIDC provider issuer URL. The standard `Authorization: Bearer <token>` header is used. Token claims:

| Claim | Purpose |
|-------|---------|
| `role` | `Operator`, `Applicant`, or `ReadOnly` |
| `customerProfileId` | Required for Applicant role; controls session ownership |

JWT validation is **disabled** when `JwtAuthority` is empty (default dev configuration). Any Bearer token is accepted in that mode.

---

## Configuration reference

| Key | Default | Description |
|-----|---------|-------------|
| `ConnectionStrings:OnboardingDb` | `Host=localhost;Port=5432;...` | PostgreSQL connection string |
| `Authentication:ApiKey` | `dev-api-key-change-in-production` | Static API key — **change in production** |
| `Authentication:JwtAuthority` | *(empty)* | OIDC issuer URL; empty disables JWT validation |
| `Authentication:JwtAudience` | `open-onboarding-api` | Expected JWT audience claim |
| `SessionTimeoutMinutes` | `1440` | Minutes before idle sessions are auto-abandoned |
| `DocumentUpload:MaxFileSizeBytes` | `10485760` | Max file size per upload (10 MB) |

---

## Node types

| Type | Behaviour |
|------|-----------|
| `Form` | Renders fields from `jsonContent`; submission validated against compliance rules |
| `Info` | Read-only information page; auto-advances with empty payload |
| `Logic` | Auto-executes `SetProfileField` or `HttpCallback` actions; supports cycle detection (max 20 auto-advances) |
| `DocumentUpload` | Accepts multipart file uploads; files stored at `wwwroot/uploads/` |
| `Redirect` | Server interpolates `{{variable}}` placeholders from session data and returns the URL |

### Compliance rule schema (`jsonContent.complianceRules`)

```json
{
  "requiredFields": ["firstName", "email"],
  "rules": [
    { "field": "email", "pattern": "^[^@]+@[^@]+$" },
    { "field": "age", "minimum": 18, "maximum": 120 },
    { "field": "country", "allowedValues": ["US", "CA", "GB"] }
  ],
  "crossFieldRules": [
    { "field1": "startDate", "operator": "LessThan", "field2": "endDate" }
  ]
}
```

### Condition operators (connection rules)

`Equals`, `NotEquals`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`, `Contains`, `NotContains`, `StartsWith`, `EndsWith`, `IsEmpty`, `IsNotEmpty`

---

## Webhooks

When a session completes, the API dispatches a signed HTTP POST to every registered webhook URL for that flow.

- **Signature**: `X-Webhook-Signature: sha256=<hex>` (HMAC-SHA256 over the JSON body using the webhook secret)
- **Retry**: 3 attempts with exponential backoff (1 s, 2 s, 4 s)
- **Payload**: `{ eventType, flowId, sessionId, customerId, completedAt, submissions }`

---

## CI/CD

Three GitHub Actions workflows are provided under `.github/workflows/`:

| Workflow | Trigger | Purpose |
|----------|---------|---------|
| `ci.yml` | PR / push to `main` | Build, test (backend + frontend lint) |
| `cd-azure.yml` | CI passes on `main` or manual | Docker image → ACR → Azure Container Apps + Static Web Apps |
| `cd-aws.yml` | CI passes on `main` or manual | Docker image → ECR → ECS + S3 + CloudFront invalidation |

Required secrets are documented as comments in each workflow file.

---

## Development

### Running tests

```bash
dotnet test src/backend/OpenOnboarding.slnx
```

### Generating new migrations

```bash
dotnet ef migrations add <Name> \
  --project src/backend/OpenOnboarding.Infrastructure \
  --startup-project src/backend/OpenOnboarding.Api
```

### Frontend validation

```bash
cd src/frontend
npm run lint
npm run build
```

### Docker build

```bash
docker build -t open-onboarding-api .
docker run -p 8080:8080 \
  -e ConnectionStrings__OnboardingDb="Host=host.docker.internal;..." \
  open-onboarding-api
```
