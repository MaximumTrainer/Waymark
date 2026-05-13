# Container Diagram (C4 Level 2)

This diagram shows the major deployable units within the Waymark system.

```mermaid
C4Container
    title Waymark — Container Diagram

    Person(operator, "Operator", "Configures flows and monitors sessions")
    Person(applicant, "Applicant", "Completes onboarding journey")

    System_Boundary(waymark, "Waymark") {
        Container(spa, "React SPA", "React 18 + TypeScript + Vite", "Flow builder UI, step renderer, analytics dashboard, session history. Communicates with API via REST and SSE.")
        Container(api, "ASP.NET Core API", ".NET 10, C#", "Hosts all REST endpoints. Handles auth, rate limiting, compliance evaluation, webhook dispatch, file uploads, SSE streaming.")
        Container(db, "PostgreSQL 16", "Relational database", "Stores flows, nodes, sessions, submissions, customer profiles, webhooks, webhook delivery logs.")
        Container(file_store, "File Store", "Local filesystem (dev) / Object Storage (prod)", "Stores uploaded documents. Files referenced by fileId in submissions.")
        Container(bg_service, "Session Timeout Service", ".NET IHostedService", "Background service: abandons sessions inactive beyond configured timeout (default: 60 minutes). Runs inside the API process.")
    }

    System_Ext(idp, "Identity Provider", "OIDC/JWT issuer (Auth0, Keycloak, etc.)")
    System_Ext(webhook_consumer, "Webhook Consumer", "Developer's system receiving lifecycle events")
    System_Ext(virus_scanner, "Virus Scanner", "ClamAV or VirusTotal API")

    Rel(operator, spa, "Uses", "HTTPS")
    Rel(applicant, spa, "Uses", "HTTPS")
    Rel(spa, api, "Calls", "HTTPS REST + Server-Sent Events")
    Rel(api, db, "Reads/writes", "TCP / PostgreSQL protocol (Npgsql)")
    Rel(api, file_store, "Stores and retrieves files", "Local filesystem / S3 API")
    Rel(api, idp, "Validates JWT tokens", "HTTPS / OIDC discovery")
    Rel(api, webhook_consumer, "Dispatches events", "HTTPS POST")
    Rel(api, virus_scanner, "Scans uploaded documents", "HTTP API")
    Rel(bg_service, db, "Reads active sessions, writes abandoned status", "PostgreSQL")
```

## Container Responsibilities

### React SPA (`src/frontend/`)
- Flow builder: drag-and-drop node/connection editor
- Step renderer: schema-driven form engine (renders fields from `jsonContent`)
- Analytics dashboard: flow performance metrics
- Session history: paginated list + detail view
- Webhook delivery inspector: delivery logs + manual retry
- Version history: snapshot diffs + rollback

### ASP.NET Core API (`src/backend/OpenOnboarding.Api/`)
- REST controllers for Flows, Customers, Webhooks, Workflow, Auth, Analytics
- Middleware: CORS, authentication, authorization, rate limiting, correlation ID
- Health probes: `/health/live`, `/health/ready`, `/health`
- Metrics: `/metrics` (Prometheus text format)
- Exception handler: maps domain exceptions to RFC 7807 ProblemDetails

### PostgreSQL Database
- Schema managed via **EF Core migrations** (`OpenOnboarding.Infrastructure/Migrations/`)
- Tables: `Flows`, `Nodes`, `Connections`, `Sessions`, `Submissions`, `CustomerProfiles`, `Webhooks`, `WebhookDeliveries`

### Session Timeout Service
- Polls every minute for sessions where `UpdatedAt < now - SessionTimeoutMinutes`
- Marks timed-out sessions as `Abandoned`
- Configured via `SessionTimeoutMinutes` (default: 60)

## Technology Stack

| Component | Technology |
|-----------|-----------|
| Frontend | React 18, TypeScript, Vite, Vitest, React Testing Library |
| Backend | .NET 10, ASP.NET Core, EF Core 10, FluentValidation |
| Database | PostgreSQL 16, Npgsql |
| Contract testing | PactNet (Rust FFI), Vitest Pact |
| CI/CD | GitHub Actions |
| Container | Docker, Docker Compose |
