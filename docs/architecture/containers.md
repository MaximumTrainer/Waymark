# Container Diagram (C4 Level 2)

This diagram shows the major deployable units within the Waymark system.

```mermaid
C4Container
    title Waymark — Container Diagram

    Person(operator, "Operator", "Configures flows and monitors sessions")
    Person(applicant, "Applicant", "Completes onboarding journey")

    System_Boundary(waymark, "Waymark") {
        Container(spa, "React SPA", "React 18 + TypeScript + Vite", "Applicant journey on the public route; flow builder, analytics and session history behind the operator SSO check. Communicates with API via REST and SSE.")
        Container(api, "ASP.NET Core API", ".NET 10, C#", "Hosts all REST endpoints. Handles auth, rate limiting, compliance evaluation, webhook dispatch, file uploads, SSE streaming.")
        Container(db, "PostgreSQL 16", "Relational database", "Stores flows, nodes, sessions, submissions, customer profiles, webhooks, webhook delivery logs.")
        Container(file_store, "File Store", "Local filesystem (dev) / Object Storage (prod)", "Stores uploaded documents. Files referenced by fileId in submissions.")
        Container(bg_service, "Session Timeout Service", ".NET IHostedService", "Background service: abandons sessions inactive beyond configured timeout (default: 1440 minutes). Runs inside the API process.")
        Container(broker, "RabbitMQ", "Message broker", "Optional, required above one API replica. Fans session events out to every instance so SSE streams stay live, and carries domain events.")
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
    Rel(api, broker, "Publishes and consumes session events", "AMQP")
```

## Container Responsibilities

### React SPA (`src/frontend/`)

Applicant route (`/`, public):
- Step renderer: schema-driven form engine (renders fields from `jsonContent`)

Operator routes (`/admin`, behind SSO — no operator surface renders on the public route):
- Flow builder: drag-and-drop node/connection editor
- Analytics dashboard: flow performance metrics
- Session history: paginated list + detail view, including the per-session analytics event trail
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
- Tables: `Flows`, `Nodes`, `Connections`, `Sessions`, `Submissions`, `CustomerProfiles`, `Webhooks`, `WebhookDeliveries`, `AnalyticsEvents`

### RabbitMQ (optional)
- Not required for a single replica; the API falls back to an in-process emitter and logs a startup
  warning outside Development.
- **Session event fan-out** — an SSE stream is pinned to the instance that accepted it, so events
  raised on another instance must travel between them. Enabled with `SessionEvents__Transport=rabbitmq`.
- Each instance binds its own exclusive, auto-delete queue to a shared fanout exchange. Messages are
  transient: an event is only useful to a stream that is open right now.
- Connection and topology recovery are enabled explicitly, so a dropped connection re-declares the
  queue and re-attaches the consumer rather than leaving a stream silently dead.

### Session Timeout Service
- Polls every minute for sessions where `UpdatedAt < now - SessionTimeoutMinutes`
- Marks timed-out sessions as `Abandoned`
- Configured via `SessionTimeoutMinutes` (default: 1440). Set it to `0` or less to disable the
  sweep entirely; the service logs that it is disabled and stops.

## Technology Stack

| Component | Technology |
|-----------|-----------|
| Frontend | React 18, TypeScript, Vite, Vitest, React Testing Library |
| Backend | .NET 10, ASP.NET Core, EF Core 10, FluentValidation |
| Database | PostgreSQL 16, Npgsql |
| Contract testing | PactNet (Rust FFI), Vitest Pact |
| CI/CD | GitHub Actions |
| Container | Docker, Docker Compose |
