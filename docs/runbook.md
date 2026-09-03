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
| `Authentication__ApiKey` | optional | — | Shared API key for X-Api-Key header auth. |
| `ApiKeys__operator` | optional | — | Per-role API keys. Key = role name, value = key. |
| `ApiKeys__applicant` | optional | — | Applicant-role API key. |
| `SessionTimeoutMinutes` | optional | `60` | Inactivity timeout before sessions are auto-abandoned. |
| `Logging__LogLevel__Default` | optional | `Information` | Log verbosity. |
| `ASPNETCORE_ENVIRONMENT` | optional | `Production` | `Development`, `Staging`, or `Production`. |

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

### Security Notes

- In non-Development environments, `Authentication__JwtAuthority` must be set — the API will fail startup if absent.
- Never log JWT tokens, API keys, or session submission content.
- File uploads are scanned before storage; rejected files return HTTP 422.
- SAML responses are rejected unless the XML signature verifies against `Authentication__Saml__IdpCertificate`, `InResponseTo` matches the AuthnRequest this server issued, the `Destination` names our ACS URL, and the assertion is within its validity window.

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

Key metrics to monitor:

| Metric | Description | Alert threshold |
|--------|-------------|-----------------|
| `dotnet_duration_seconds` | HTTP request duration | p95 > 2s |
| `waymark_session_started_total` | New sessions started | Sudden drop |
| `waymark_session_completed_total` | Sessions completed | Sudden drop |
| `waymark_session_abandoned_total` | Sessions abandoned by timeout | Spike > normal |
| `waymark_webhook_delivery_failed_total` | Failed webhook deliveries | > 5 in 5 min |
| `waymark_webhook_delivery_retried_total` | Webhook retries | Sustained high rate |
| `process_cpu_seconds_total` | Process CPU usage | > 80% for 5 min |

**Grafana dashboard** (if using Grafana Cloud / self-hosted):
- Import the provided dashboard JSON from `docs/grafana-dashboard.json` (if available)
- Data source: Prometheus scraping `http://api:8080/metrics` every 15s

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

The API is **stateless** with one caveat: `InMemorySessionEventEmitter` (SSE) holds per-session event channels in memory. In a multi-instance setup:
- SSE clients must connect to the same instance that holds their session's channel, **or**
- Replace `InMemorySessionEventEmitter` with a distributed adapter (e.g., Redis Pub/Sub)

All other state is in PostgreSQL — safe for horizontal scaling.

**Connection pool**: Configure `Maximum Pool Size` in the connection string (default: 100). For multi-instance, ensure total connections < PostgreSQL `max_connections` (default: 100).

### Rate Limits

Default rate limit: **100 requests per minute per IP** (configurable). Adjust in `Program.cs` `AddRateLimiter()`.
