# Waymark — Operations Runbook

This runbook covers deployment, monitoring, and operational procedures for the Waymark onboarding platform.

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Deployment](#deployment)
3. [Configuration Reference](#configuration-reference)
4. [Health Monitoring](#health-monitoring)
5. [Metrics](#metrics)
6. [Database Operations](#database-operations)
7. [Incident Response](#incident-response)
8. [Backup and Recovery](#backup-and-recovery)
9. [Scaling](#scaling)

---

## Architecture Overview

Waymark consists of:
- **API**: ASP.NET Core .NET 10 application (Docker container)
- **Database**: PostgreSQL 16
- **Frontend**: React SPA (static files served via CDN or web server)

See [architecture diagrams](architecture/README.md) for C4 diagrams.

---

## Deployment

### Prerequisites

- Docker 24+
- PostgreSQL 16
- .NET 10 SDK (for local builds only)
- GitHub Actions with secrets configured (for CI/CD)

### Docker Compose (local / staging)

```bash
# Start all services (API + PostgreSQL)
docker compose up -d

# Check service health
docker compose ps

# View API logs
docker compose logs api -f

# Stop
docker compose down
```

The API applies EF Core migrations automatically on startup via `db.Database.MigrateAsync()`.

### Azure Container Apps (production)

Deployment is automated via `.github/workflows/cd-azure.yml` after CI passes on `main`.

**Manual deploy:**
```bash
# Build and push image
az acr build --registry $ACR_LOGIN_SERVER --image open-onboarding-api:$SHA .

# Update container app
az containerapp update \
  --name $AZURE_CONTAINER_APP_NAME \
  --resource-group $AZURE_RESOURCE_GROUP \
  --image $ACR_LOGIN_SERVER/open-onboarding-api:$SHA
```

### AWS ECS (production)

Deployment is automated via `.github/workflows/cd-aws.yml` after CI passes on `main`.

**Manual deploy:**
```bash
# Build and push image
aws ecr get-login-password --region $AWS_REGION | docker login --username AWS --password-stdin $ECR_REGISTRY
docker build -t $ECR_REGISTRY/$ECR_REPOSITORY:$SHA .
docker push $ECR_REGISTRY/$ECR_REPOSITORY:$SHA

# Update ECS service
aws ecs update-service --cluster $ECS_CLUSTER --service $ECS_SERVICE \
  --task-definition $ECS_TASK_DEFINITION --force-new-deployment
aws ecs wait services-stable --cluster $ECS_CLUSTER --services $ECS_SERVICE
```

---

## Configuration Reference

All configuration can be set via environment variables or `appsettings.json`.

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `ConnectionStrings__OnboardingDb` | ✅ | — | PostgreSQL connection string |
| `Authentication__JwtAuthority` | ✅ (non-dev) | — | OIDC issuer URL. Empty string = JWT disabled (dev only). |
| `Authentication__JwtAudience` | ✅ (non-dev) | — | Expected JWT audience claim. |
| `Authentication__ApiKey` | optional | — | **Server-to-server only.** Grants a full Operator principal — never send it from a browser. See [API credentials](#api-credentials). |
| `Authentication__ApplicantToken__SigningKey` | ✅ (non-dev) | — | HMAC key signing per-session applicant tokens, min 32 chars. **Store as a secret.** Startup fails without it outside Development. |
| `Authentication__ApplicantToken__LifetimeMinutes` | optional | `SessionTimeoutMinutes` | Applicant token lifetime. Defaults to the session timeout, since the token is useless once its session is abandoned. |
| `SessionTimeoutMinutes` | optional | `1440` | Inactivity timeout before sessions are auto-abandoned. Also the default applicant token lifetime. Set to `0` or less to disable the sweep. |
| `Analytics__DatabaseProvider__Enabled` | optional | `true` | Persist analytics events so a session's trail can be read back. `false` leaves only the console provider. |
| `Analytics__ConsoleProvider__Enabled` | optional | `true` | Write every event to the application log. |
| `Analytics__RetentionDays` | optional | `365` | Age at which stored analytics events are deleted. |
| `Analytics__CleanupIntervalHours` | optional | `24` | How often the analytics retention sweep runs. |
| `RateLimiting__AnalyticsIngestPerMinute` | optional | `120` | Per-caller limit on `POST /api/analytics/events`. |
| `Logging__LogLevel__Default` | optional | `Information` | Log verbosity. |
| `ASPNETCORE_ENVIRONMENT` | optional | `Production` | `Development`, `Staging`, or `Production`. |

### API credentials

Three credentials reach the API, and they are not interchangeable.

| Credential | Who holds it | Grants |
|------------|--------------|--------|
| Applicant session token | The applicant's browser | The one session named in the token. Nothing else. |
| `AdminSession` cookie | An operator's browser, after SAML SSO | Operator role, subject to the SSO NameID allowlist. |
| `Authentication__ApiKey` | Server-to-server integrations | A full Operator principal. |

**The API key must never be sent from a browser.** It maps to an Operator principal with no further
checks, so a key present in a page is a key every visitor to that page holds. The public onboarding
app does not use it: it starts a session anonymously and is handed a token for that session.

#### Applicant session tokens

`POST /api/workflow/sessions/start` is anonymous — it is the entry point of a public journey, so
there is no credential to present yet. It is rate limited by the `session-start` policy. The
response carries `applicantToken`, an HMAC-signed JWT the browser sends as
`Authorization: Bearer <token>` for the rest of the journey.

- **Scope.** The token names one session. It is rejected for any other session, and for every
  operator endpoint, including the submissions readout for its own session.
- **Lifetime.** Defaults to `SessionTimeoutMinutes` (1440), overridable with
  `Authentication__ApplicantToken__LifetimeMinutes`. A shorter life only strands applicants
  mid-journey; a longer one outlives the session it names, which is abandoned by then anyway.
- **Renewal.** Step submission returns a fresh `applicantToken` and `applicantTokenExpiresAt`
  alongside the next step, and the browser stores it in place of the one it held. Activity is what
  extends the credential, so a journey being worked on never outlives its own token, and there is no
  separate refresh endpoint to protect. Operators get no token back: their own credential already
  covers the session.
- **Documents.** Upload and download are scoped to the session in the route, like every other
  session endpoint. Upload follows the terminal-status rule below. Download also scopes the
  *lookup*: a file id is resolved only if an upload recorded against that session and node carries
  it, so holding an id for another applicant's document does not make it readable through your own
  session. A file that exists but belongs elsewhere returns `404`, never `403` — a refusal would
  confirm the id is real.
- **Terminal sessions.** Once a session is `Completed` or `Abandoned` its token is refused for
  writes — step submission, document upload and `POST /api/analytics/events` all return `403`. Reads still work, so
  the completion screen renders. This bounds how long a credential left behind on a shared machine
  stays useful: the visit, not the full lifetime. Operators are unaffected; the rule is about a
  stale applicant credential, not about who may act on a finished session. Abandoning an already
  terminal session stays idempotent rather than returning `403`, since it changes nothing.
- **Clearing.** The browser drops the token from `sessionStorage` when the journey completes.
- **Expiry, from the applicant's side.** An expired token returns `401`; a token for a finished
  session returns `403`. The frontend treats both as an expired credential and shows a distinct
  "your session has expired, start again" state rather than a generic error banner — an expired
  credential is a dead end, not a fault to retry.
- **Signing key.** `Authentication__ApplicantToken__SigningKey` must be set outside Development or
  startup fails. All replicas need the same key, or a token issued by one is rejected by another.
  In Development an ephemeral key is generated per process, so tokens stop working on restart.
- **SSE.** The browser `EventSource` API cannot set request headers, so
  `GET /api/workflow/sessions/{id}/events` also accepts the token as an `access_token` query
  parameter. Ownership is enforced identically on both paths.

### Journey analytics

Two things are counted separately.

**Aggregate flow figures** — completion rate, drop-off, average duration — are derived from sessions
and submissions on demand at `GET /api/analytics/flows/{flowId}` (operator only). They need no
event history and are unaffected by retention.

**The event trail** is the per-step record of what happened in one session, readable at
`GET /api/analytics/sessions/{sessionId}/events` (operator only), ordered by occurrence.

- Server-raised events (session started, step advanced) are emitted by the journey engine.
- Client-raised events are posted by the browser to `POST /api/analytics/events` in batches. The
  caller's applicant token must name the session every event in the batch belongs to, or the whole
  batch is rejected with `403` — a client cannot write events against someone else's journey. The
  server stamps `source`, so a client cannot pass its events off as server-raised.
- Delivery from the browser is best-effort: a failed batch is dropped rather than retried, so a
  broken analytics endpoint can never stall an application form.

Events are stored in the `AnalyticsEvents` table and pruned by a background sweep after
`Analytics__RetentionDays`, measured from **write** time rather than the client-reported timestamp,
which a caller controls. Set `Analytics__DatabaseProvider__Enabled=false` to turn storage off
entirely; the application keeps working and events go only to the log.

#### Reading one session's trail

The operator console shows a session's trail under **Sessions → (a session) → Event trail**, beside
its submissions. Submissions say what the applicant entered; the trail says how they got there —
the order steps were seen in, and the gap between consecutive events, which is what makes a stall
visible. It reads `GET /api/analytics/sessions/{sessionId}/events` with the admin session cookie,
and is never requested from the applicant page.

Two empty states are worth recognising when reading it:

- **Server events only.** Client events are self-reported by the applicant's browser and can be
  absent entirely — an ad blocker or a tab closed before the batch flushed stops them reaching the
  API. The panel says so. It is not evidence of a fault.
- **No events at all.** Either none were raised, or they aged past `Analytics__RetentionDays` and
  were deleted. The two are indistinguishable after the sweep, so the panel names both rather than
  implying data was lost. Expect this for any session older than the retention window.

Aggregate drop-off in the flow analytics view is still derived from session status and submissions,
not from this event stream, so per-step timing is currently visible per session but not across a
flow.

### SAML Single Sign-On (Admin UI)

The admin UI signs operators in over SAML 2.0 (`GET /auth/saml/login` → IdP → `POST /auth/saml/callback`).
Import `GET /auth/saml/metadata` into the IdP to register Waymark as a service provider.

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `Authentication__Saml__Issuer` | ✅ | `waymark-service-provider` | SP entity ID. Must match the IdP's configured audience. |
| `Authentication__Saml__IdpSsoUrl` | ✅ | — | IdP single sign-on endpoint the `AuthnRequest` is redirected to. |
| `Authentication__Saml__IdpCertificate` | ✅ | — | PEM of the IdP signing certificate used to verify assertions. |
| `Authentication__Saml__SpCertificate` | ✅ | — | PEM of the SP signing certificate published in metadata. |
| `Authentication__Saml__SpPrivateKey` | ✅ | — | PEM of the SP private key. **Store as a secret**, never in `appsettings.json`. |
| `Authentication__Saml__AllowedNameIds__0` | ✅ | — | Allowlist of NameIDs permitted to sign in. An empty list denies everyone. |
| `Authentication__Saml__AcsUrl` | optional | request origin + `/auth/saml/callback` | Override when the public URL differs from the request host (e.g. behind a proxy). |
| `Authentication__Saml__AllowedReturnOrigins__0` | optional | — | Absolute origins accepted for `returnUrl`; anything else falls back to a relative path. |
| `Authentication__Saml__RelayStateTimeoutMinutes` | optional | `5` | Lifetime of the relay-state and AuthnRequest-ID cookies. |
| `Authentication__Saml__SessionDurationHours` | optional | `8` | Admin session lifetime after a successful assertion. |
| `Authentication__Saml__RequireEncryptedAssertion` | optional | `false` | When `true`, an unencrypted assertion is rejected with `saml_assertion_not_encrypted`. |

### Security Notes

- In non-Development environments, `Authentication__JwtAuthority` must be set — the API will fail startup if absent.
- Never log JWT tokens, API keys, or session submission content.
- File uploads are scanned before storage; rejected files return HTTP 422.
- SAML responses are rejected unless the XML signature verifies against `Authentication__Saml__IdpCertificate`, `InResponseTo` matches the AuthnRequest this server issued, the `Destination` names our ACS URL, and the assertion is within its validity window.

#### Assertion encryption

`GET /auth/saml/metadata` publishes the SP certificate under both `KeyDescriptor use="signing"` and
`KeyDescriptor use="encryption"`, so an IdP may encrypt the assertion to it. The same
`Authentication__Saml__SpCertificate` / `Authentication__Saml__SpPrivateKey` pair is registered as the
decryption key, so **no extra configuration is needed** to accept encrypted assertions — enable
encryption on the IdP side and it works.

Set `Authentication__Saml__RequireEncryptedAssertion=true` to reject assertions that arrive
unencrypted. Leave it unset while migrating an IdP to encryption, then turn it on once the IdP is
confirmed to be encrypting.

#### SAML login error codes

The callback redirects to `/login?error=<code>` on failure. Each code has a distinct cause:

| Code | Cause |
|------|-------|
| `saml_csrf_failed` | `RelayState` did not match the cookie issued at login. |
| `saml_certificate_expired` | The configured SP or IdP certificate is past its `NotAfter`. Rotate it. |
| `saml_assertion_not_encrypted` | `RequireEncryptedAssertion` is enabled and the assertion was not encrypted. |
| `saml_access_denied` | The assertion's NameID is not in `Authentication__Saml__AllowedNameIds`. |
| `saml_invalid_assertion` | Signature, `InResponseTo`, `Destination`, validity window, or decryption failed. |

`saml_invalid_assertion` is deliberately generic to the browser; the server log distinguishes a
decryption failure from a signature failure.

---

## Health Monitoring

The API exposes three health probe endpoints:

| Endpoint | Use | Checks |
|----------|-----|--------|
| `GET /health/live` | Kubernetes/ECS liveness | Returns 200 if app is running |
| `GET /health/ready` | Kubernetes/ECS readiness | Returns 200 if DB is reachable |
| `GET /health` | Detailed status | DB check + disk (optional) |

**Expected healthy response** (`/health/ready`):
```json
{"status":"Healthy","results":{"database":{"status":"Healthy"}}}
```

**Unhealthy** (DB unreachable):
```json
{"status":"Unhealthy","results":{"database":{"status":"Unhealthy","description":"..."}}}
```

Docker Compose configures automatic restart when the liveness probe fails.

---

## Metrics

The API exposes Prometheus metrics at `GET /metrics`.

The endpoint requires authentication and is exempt from rate limiting.

### Application metrics

These are the metrics the application emits. Names are as registered in
`PrometheusMetricsService`; build alerts against these exact strings.

| Metric | Type | Labels | Emitted when | Alert on |
|--------|------|--------|--------------|----------|
| `onboarding_sessions_started_total` | counter | `flowId` | A session starts | Sudden drop |
| `onboarding_sessions_completed_total` | counter | `flowId` | A session completes | Sudden drop, or a widening gap against started |
| `onboarding_webhook_deliveries_total` | counter | `status` (`delivered`, `failed`) | Each delivery attempt resolves | `failed` rate > 5 in 5 min |
| `waymark_virus_scan_bypassed_total` | counter | — | A document upload is accepted without a real scan | Any increase outside a deliberately unscanned environment |

`prometheus-net` also exports standard .NET runtime and process metrics on the same endpoint, among
them `process_cpu_seconds_total` (alert above 80% for 5 min) and the GC and thread-pool series.

### What is not exposed

Worth knowing before you design a dashboard around metrics that will never arrive:

- **HTTP request duration and status code counts.** `UseHttpMetrics()` is not wired up, so there is
  no `http_request_duration_seconds` series and no per-endpoint latency or error rate. Request-level
  monitoring has to come from your ingress or APM until that middleware is added.
- **Abandoned sessions.** There is no counter for sessions the timeout sweep abandons. Track
  abandonment from the `Sessions` table or the analytics event trail instead.
- **`onboarding_active_sessions`.** The gauge is registered but nothing ever sets it, so it reads
  zero permanently. Do not alert on it.

### Grafana

There is no dashboard JSON in this repository; build one against the metric names above.

- Data source: Prometheus scraping `http://api:8080/metrics` every 15s.
- The scraper needs a credential, since `/metrics` requires authentication.

---

## Database Operations

### Migrations

Migrations run automatically on startup. To run manually:

```bash
# From src/backend/
dotnet ef database update --project OpenOnboarding.Infrastructure --startup-project OpenOnboarding.Api
```

To generate a new migration:
```bash
dotnet ef migrations add <MigrationName> \
  --project OpenOnboarding.Infrastructure \
  --startup-project OpenOnboarding.Api
```

### Connection String Formats

```
# Local / Docker Compose
Host=localhost;Port=5432;Database=onboarding;Username=postgres;Password=postgres

# Azure Database for PostgreSQL
Host=<server>.postgres.database.azure.com;Port=5432;Database=onboarding;Username=<user>;Password=<pass>;Ssl Mode=Require

# AWS RDS PostgreSQL
Host=<rds-endpoint>;Port=5432;Database=onboarding;Username=<user>;Password=<pass>;
```

### Useful Queries

```sql
-- Active sessions (in progress)
SELECT id, flow_id, customer_profile_id, status, created_at, updated_at
FROM "Sessions" WHERE status = 0 ORDER BY updated_at DESC LIMIT 50;

-- Webhook delivery failures in last hour
SELECT w.url, d.event_type, d.attempt_count, d.created_at, d.response_status
FROM "WebhookDeliveries" d
JOIN "Webhooks" w ON w.id = d.webhook_id
WHERE d.status = 2 AND d.created_at > now() - interval '1 hour';

-- Flows with abandonment rate > 50%
SELECT flow_id,
  COUNT(*) FILTER (WHERE status = 2) AS abandoned,
  COUNT(*) AS total,
  ROUND(100.0 * COUNT(*) FILTER (WHERE status = 2) / NULLIF(COUNT(*), 0), 1) AS abandonment_pct
FROM "Sessions"
GROUP BY flow_id
HAVING 100.0 * COUNT(*) FILTER (WHERE status = 2) / NULLIF(COUNT(*), 0) > 50;
```

---

## Incident Response

### API Not Responding

1. Check health: `curl https://<host>/health/live`
2. Check container: `docker compose ps` / ECS console
3. Check logs: `docker compose logs api --tail 100`
4. Check DB connectivity: confirm PostgreSQL is reachable from the API container
5. Restart if healthy: `docker compose restart api`

### Database Connection Failures

1. Verify connection string environment variable is set correctly
2. Check PostgreSQL is running: `docker compose ps postgres`
3. Check PostgreSQL logs: `docker compose logs postgres --tail 50`
4. Verify credentials: `psql "Host=...;..."` 
5. Check for connection pool exhaustion — look for `Npgsql.NpgsqlException: connection pool`

### Webhook Delivery Failures

1. Query failing webhooks:
   ```sql
   SELECT * FROM "WebhookDeliveries" WHERE status = 2 ORDER BY created_at DESC LIMIT 20;
   ```
2. Check consumer endpoint reachability from the API container
3. Check for HTTP 4xx responses (indicates consumer-side config issue vs. network issue)
4. Manual retry via API: `POST /webhooks/deliveries/{id}/retry`
5. If HMAC signature is failing, verify consumer is validating `X-Waymark-Signature` correctly

### High Session Abandonment Rate

1. Query abandonment by node to find the bottleneck:
   ```sql
   SELECT current_node_id, COUNT(*) AS count
   FROM "Sessions" WHERE status = 2
   GROUP BY current_node_id ORDER BY count DESC;
   ```
2. Review the problematic node's compliance rules — may be too strict
3. Check SSE streaming is working (clients not stuck waiting for events)

---

## Backup and Recovery

### Database Backups

**Docker Compose (manual):**
```bash
docker compose exec postgres pg_dump -U postgres onboarding > backup-$(date +%Y%m%d).sql
```

**Restore:**
```bash
docker compose exec -T postgres psql -U postgres onboarding < backup-20241201.sql
```

**AWS RDS**: Enable automated backups (7-day retention recommended) + manual snapshots before migrations.

**Azure Database for PostgreSQL**: Enable geo-redundant backups in server configuration.

### Recovery Procedure

1. Stop the API: `docker compose stop api`
2. Drop and recreate the database:
   ```bash
   docker compose exec postgres psql -U postgres -c "DROP DATABASE onboarding; CREATE DATABASE onboarding;"
   ```
3. Restore from backup:
   ```bash
   docker compose exec -T postgres psql -U postgres onboarding < backup.sql
   ```
4. Start the API (migrations run automatically): `docker compose start api`

---

## Scaling

### Horizontal Scaling (Multiple API Instances)

The API is **stateless** with one caveat: an SSE stream is pinned to the instance that accepted it,
so a session event raised while handling a request on another instance has to travel between
instances to reach that stream.

**Running more than one replica requires a distributed session event transport.** Without one the
failure is silent: the stream stays open, no error is raised, and the applicant simply never
receives step progress until they reload. A rolling deploy or scale-in re-breaks it mid-journey, so
sticky sessions are not a substitute.

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `SessionEvents__Transport` | ✅ for >1 replica | — | `rabbitmq` enables the distributed emitter. Unset uses the in-memory emitter. |
| `SessionEvents__RabbitMq__Uri` | optional | `EventBus__RabbitMq__Uri`, else `amqp://guest:guest@localhost:5672/` | Broker connection. Defaults to the event bus broker so an existing RabbitMQ deployment needs only `SessionEvents__Transport`. |
| `SessionEvents__RabbitMq__Exchange` | optional | `waymark-session-events` | Fanout exchange name. Each instance binds its own exclusive auto-delete queue. |

A non-Development environment running the in-memory emitter logs a startup warning naming this
limitation. Events are transient and not persisted: they are only useful to a stream that is open
at the time, so an instance that was down missed the stream too.

**Connection recovery.** The transport sets `AutomaticRecoveryEnabled` and `TopologyRecoveryEnabled`
explicitly rather than relying on the RabbitMQ client defaults. On a dropped connection the client
reconnects, re-declares the exclusive queue and its binding, and re-attaches the consumer, so
delivery resumes without restarting the instance. This is covered by a test that drops the
connection from the broker side and asserts events flow again. Each connection reports a name of
`open-onboarding-session-events:<hostname>`, so the management UI identifies which replica holds
which queue.

**Testing the transport.** The broker-backed tests skip themselves when nothing is listening, so
`dotnet test` stays dependency-free. To run them:

```bash
docker compose up -d rabbitmq
dotnet test src/backend/OpenOnboarding.Application.Tests
```

Point them elsewhere with `SESSIONEVENTS__RABBITMQ__URI`. One of them — the recovery test — also
needs the management API on port 15672, and skips separately if the broker image does not include
it. CI declares a `rabbitmq:3-management` service container and sets `REQUIRE_BROKER_TESTS=1`, which
turns a missing broker into a failure rather than a skip: a silently skipped test is a green build
that proved nothing.

All other state is in PostgreSQL — safe for horizontal scaling.

**Connection pool**: Configure `Maximum Pool Size` in the connection string (default: 100). For multi-instance, ensure total connections < PostgreSQL `max_connections` (default: 100).

### Rate Limits

Every policy is **partitioned per caller**, so one client exhausting its budget does not affect any
other. Configure the limits under `RateLimiting` in `appsettings.json` or as
`RateLimiting__<Key>` environment variables.

| Policy | Endpoint | Partitioned by | Setting | Default (per minute) |
| --- | --- | --- | --- | --- |
| `session-start` | `POST /api/workflow/sessions/start` | Client IP — the endpoint is anonymous by design | `RateLimiting:SessionStartPerMinute` | 100 |
| `analytics-ingest` | `POST /api/analytics/events` | Applicant session id from the token; operators fall back to their principal | `RateLimiting:AnalyticsIngestPerMinute` | 120 |
| `webhook-registration` | `POST /api/flows/{flowId}/webhooks` | Authenticated principal, falling back to client IP | `RateLimiting:WebhookRegistrationPerMinute` | 20 |
| `general` | every other controller endpoint | Authenticated principal, falling back to client IP | `RateLimiting:GeneralPerMinute` | 300 |
| global ceiling | every request | nothing — one bucket for the whole instance | `RateLimiting:GlobalCeilingPerMinute` | 3000 |

The global ceiling runs **in addition to** the endpoint policy, so a flood spread across thousands
of partitions still has a bound. A request rejected by either returns `429` with `Retry-After: 60`.

`general` is the fallback: it is attached to every controller endpoint that does not declare a
policy of its own, so no part of the API is unlimited. An endpoint naming a specific policy is
**not** additionally bound by it — the two do not stack, and `session-start` runs under its own
budget alone. Health check endpoints are outside it entirely, since throttling the endpoint a load
balancer polls turns a traffic spike into an instance being pulled out of service.

One consequence worth sizing for: callers sharing a credential share a partition. Every integration
using the same API key spends one `general` budget between them, because the partition key is the
principal. Give separate integrations separate credentials, or raise the limit to cover them all.

#### Behind a proxy

The partition key is the connection address, which behind a load balancer is the balancer's own
address — every caller would land in one partition again. `X-Forwarded-For` corrects this, but only
from a proxy this API trusts; a header from anywhere else is an anonymous caller choosing their own
partition key, and is ignored.

**Nothing is trusted by default.** The ASP.NET defaults (loopback) are cleared at startup, so
`X-Forwarded-For` has no effect until you name your proxy:

```jsonc
"ForwardedHeaders": {
  "KnownProxies": ["10.1.2.3"],        // individual proxy addresses
  "KnownNetworks": ["10.0.0.0/8"],     // or CIDR ranges
  "ForwardLimit": 1                     // hops to walk back; raise only if you have chained proxies
}
```

If you deploy behind an ingress and leave this unset, every request partitions by the ingress
address and the limits behave as global ones. Set it.

#### Multi-replica behaviour

**Limits are per-instance, not cluster-wide.** The limiters hold their counters in process memory,
so with *N* replicas a caller's effective budget is up to *N* × the configured value, depending on
which instance each request lands on. This is accepted deliberately, for the same reason as the SSE
fan-out design: the alternative is a shared counter store on the path of every request.

Size the limits accordingly — divide the budget you actually want by your replica count — and treat
these as a coarse abuse bound, not a precise quota. A cluster-wide limit belongs at the ingress or
in a shared store; if you need one, that is where to put it.
