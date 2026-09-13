# Waymark — Troubleshooting Guide

Common issues and how to resolve them.

---

## Table of Contents

- [API Startup Issues](#api-startup-issues)
- [Authentication Errors](#authentication-errors)
- [Rate Limiting](#rate-limiting)
- [Database Issues](#database-issues)
- [Workflow / Session Errors](#workflow--session-errors)
- [Webhook Issues](#webhook-issues)
- [File Upload Issues](#file-upload-issues)
- [SSE (Server-Sent Events) Issues](#sse-server-sent-events-issues)
- [Frontend Issues](#frontend-issues)
- [CI / GitHub Actions Failures](#ci--github-actions-failures)

---

## API Startup Issues

### API fails to start: "JwtAuthority is required in non-Development environments"

**Cause**: The `Authentication:JwtAuthority` configuration value is empty or missing, and the app is not running in the `Development` environment.

**Resolution**:
- Set `Authentication__JwtAuthority` to your OIDC issuer URL (e.g., `https://your-auth0-tenant.auth0.com/`).
- If you intentionally want to run without JWT (local dev), set `ASPNETCORE_ENVIRONMENT=Development`.

---

### API fails to start: "An error occurred while applying the migrations"

**Cause**: The API cannot connect to PostgreSQL on startup, or the migration contains a conflict with existing schema.

**Resolution**:
1. Check the connection string: `ConnectionStrings__OnboardingDb`
2. Verify PostgreSQL is running and reachable
3. Check for migration conflicts:
   ```bash
   dotnet ef migrations list \
     --project src/backend/OpenOnboarding.Infrastructure \
     --startup-project src/backend/OpenOnboarding.Api
   ```
4. If a migration is partially applied, you may need to revert to the last good state:
   ```bash
   dotnet ef database update <PreviousMigrationName> \
     --project src/backend/OpenOnboarding.Infrastructure \
     --startup-project src/backend/OpenOnboarding.Api
   ```

---

### API starts but immediately crashes (OOMKilled)

**Cause**: Container memory limit is too low, or a memory leak.

**Resolution**:
- Increase container memory limit to at least 512MB (recommended: 1GB)
- Check for N+1 queries — use EF Core logging to identify: set `Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Information`

---

## Authentication Errors

### HTTP 401 on all requests

**Symptoms**: All API calls return `401 Unauthorized`.

**Resolution**:
1. Verify you are sending `Authorization: Bearer <token>` or `X-Api-Key: <key>` header.
2. If using JWT:
   - Verify the token is not expired: decode at [jwt.io](https://jwt.io)
   - Verify `Authentication__JwtAuthority` matches the `iss` claim in your token
   - Verify `Authentication__JwtAudience` matches the `aud` claim
3. If using API key: verify the key matches `Authentication__ApiKey` or one of `ApiKeys__*` config values.

---

### HTTP 403 Forbidden

**Symptoms**: Request is authenticated but returns `403 Forbidden`.

**Cause**: The caller is authenticated but not entitled to this operation on this resource. There
are three distinct causes, and they need different fixes.

**Resolution**:

1. **Wrong role.** Check the caller's claims with `GET /api/auth/me`. Operator-only endpoints (flow
   CRUD, webhooks, analytics reads) require the `Operator` role.
2. **Right role, wrong session.** An applicant token names exactly one session and is refused for
   any other — including document upload and download, which are scoped to the session in the
   route. Confirm the `sessionId` in the path matches the token you are sending.
3. **The session is finished.** Once a session reaches `Completed` or `Abandoned`, its own token is
   refused for *writes* — step submission, document upload and `POST /api/analytics/events` all
   return `403`. Reads still succeed, which is what lets a completion screen render. This is
   expected; the journey must be restarted rather than resumed.

A browser client should treat `401` (expired) and `403` on a session it was completing as the same
outcome — the credential is spent — and both as distinct from a `5xx`, which is retryable.

> A document that exists but is not recorded against the session in the route returns `404`, not
> `403`. That is deliberate: a `403` would confirm the file id names a real document.

---

### HTTP 401 with "Bearer error="invalid_token""

**Cause**: JWT signature validation failed.

**Resolution**:
- Ensure `Authentication__JwtAuthority` URL matches exactly (trailing slash matters)
- Verify the OIDC discovery document is reachable: `curl https://<authority>/.well-known/openid-configuration`
- Check for clock skew: ensure server time is synchronized (NTP). JWTs are rejected if the server clock is more than 5 minutes ahead of the token's `iat`.

---

## Rate Limiting

### HTTP 429 Too Many Requests

**Symptoms**: Requests return `429` with a `Retry-After: 60` header and a problem-details body.

**Cause**: A rate limit policy's budget for *your partition* is exhausted. Limits are partitioned
per caller, so this is about one caller's traffic, not the service being globally busy.

**Resolution**:

1. Identify which policy applies. `general` covers every controller endpoint that does not name a
   tighter one, so most endpoints fall under it.

   | Policy | Applies to | Partitioned by | Setting | Default |
   |---|---|---|---|---|
   | `session-start` | `POST /api/workflow/sessions/start` | Client IP | `RateLimiting__SessionStartPerMinute` | 100 |
   | `analytics-ingest` | `POST /api/analytics/events` | Applicant session | `RateLimiting__AnalyticsIngestPerMinute` | 120 |
   | `webhook-registration` | webhook registration | Principal | `RateLimiting__WebhookRegistrationPerMinute` | 20 |
   | `general` | everything else | Principal, else client IP | `RateLimiting__GeneralPerMinute` | 300 |
   | global ceiling | every request | not partitioned | `RateLimiting__GlobalCeilingPerMinute` | 3000 |

2. Check whether callers are **sharing a partition**. The two common causes:
   - **A shared credential.** Every integration using the same API key spends one `general` budget
     between them, because the partition key is the principal. Give each integration its own
     credential.
   - **An unconfigured proxy.** Behind a load balancer the connection address is the balancer's, so
     every caller collapses into one partition. Set `ForwardedHeaders__KnownProxies` (or
     `ForwardedHeaders__KnownNetworks`) to your proxy's address. Until you do, `X-Forwarded-For` is
     deliberately ignored — an untrusted header would let any caller choose their own partition key.
3. Raise the relevant limit if the traffic is legitimate. Remember limits are **per-instance**: with
   *N* replicas the effective budget is up to *N* × the configured value, so size accordingly.

---

### Rate limits appear to do nothing

**Cause**: Limiters are disabled entirely in the `Testing` environment, where every policy is
replaced with a no-op so unrelated tests are not throttled.

**Resolution**: Run in `Development` or a real environment to exercise them. Test suites that drive
the API hard from a single address set large budgets explicitly rather than relying on this.

---

### Health checks are being rate limited

They are not — `/health`, `/health/live` and `/health/ready` are deliberately outside rate limiting.
Throttling the endpoint a load balancer polls would turn a traffic spike into an instance being
pulled out of service. If health checks are failing, the cause is elsewhere.

---

## Database Issues

### "FATAL: remaining connection slots are reserved for non-replication superuser connections"

**Cause**: PostgreSQL connection pool is exhausted.

**Resolution**:
1. Check current connections:
   ```sql
   SELECT count(*) FROM pg_stat_activity;
   ```
2. Reduce `Maximum Pool Size` in the connection string (e.g., `Maximum Pool Size=20`)
3. Ensure `OnboardingDbContext` is `Scoped` (not `Singleton`) — it is registered correctly by default
4. Increase PostgreSQL `max_connections` (requires restart): edit `postgresql.conf`

---

### "relation does not exist" errors

**Cause**: Migrations have not been applied to the database.

**Resolution**:
```bash
dotnet ef database update \
  --project src/backend/OpenOnboarding.Infrastructure \
  --startup-project src/backend/OpenOnboarding.Api
```
Or restart the API container (it calls `MigrateAsync()` on startup).

---

### Slow queries / high CPU on PostgreSQL

**Resolution**:
1. Enable query logging in PostgreSQL: set `log_min_duration_statement = 1000` (log queries > 1s)
2. Run `EXPLAIN ANALYZE` on slow queries
3. Check missing indexes — common candidates: `Sessions.Status`, `Sessions.FlowId`, `Submissions.SessionId`
4. Run `VACUUM ANALYZE` on large tables:
   ```sql
   VACUUM ANALYZE "Sessions";
   VACUUM ANALYZE "Submissions";
   ```

---

## Workflow / Session Errors

### POST /sessions/start returns HTTP 404

**Cause**: The referenced `flowId` does not exist.

**Resolution**: Verify the flow exists: `GET /flows/{flowId}`. Check for typos in the UUID.

---

### POST /sessions/{id}/steps/{nodeId}/submit returns HTTP 422

**Cause**: Compliance rule validation failed for the submitted data.

**Response body** (RFC 7807 ProblemDetails):
```json
{
  "type": "https://waymark.ai/errors/compliance-violation",
  "title": "Compliance violation",
  "status": 422,
  "violations": [
    { "field": "annualRevenue", "message": "Value must be between 0 and 999999999", "ruleId": "maxValue" }
  ]
}
```

**Resolution**:
- Review the `violations` array in the response
- Update the submitted data to satisfy the compliance rules
- If the rules are incorrect, update the node's `ComplianceRuleJson` via `PUT /flows/{id}/nodes/{nodeId}`

---

### Session status is "Abandoned" unexpectedly

**Cause**: The session timeout background service marked the session as abandoned because it was inactive beyond `SessionTimeoutMinutes` (default: 1440 minutes, i.e. 24 hours).

**Resolution**:
- Start a new session
- If this is happening too quickly, increase `SessionTimeoutMinutes` in configuration
- If the session was active (user was working), check for SSE connection issues that may have caused the frontend to stop sending activity

---

### Step submission returns HTTP 409 Conflict

**Cause**: The node being submitted is not the session's current node.

**Resolution**: Use `GET /sessions/{id}/next` to get the correct current node ID, then submit to that node.

---

## Webhook Issues

### Webhooks not being delivered

1. Verify the webhook is registered: `GET /webhooks`
2. Check delivery log: `GET /webhooks/{id}/deliveries`
3. Verify the consumer URL is reachable from the API server (not `localhost`)
4. Check for SSRF protection rejecting the URL — private IP ranges are blocked

---

### HTTP 400 when registering webhook: "Invalid URL"

**Cause**: The webhook URL is on a private network (SSRF protection).

**Blocked ranges**: `127.0.0.0/8`, `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `::1`

**Resolution**: Use a publicly accessible URL. For local testing, use [ngrok](https://ngrok.com) or a similar tunneling service.

---

### Consumer receiving webhooks but HMAC signature validation fails

**Cause**: Consumer is not validating the `X-Waymark-Signature` header correctly.

**Expected format**: `sha256=<hex_digest>`

**Validation example (C#)**:
```csharp
var secret = Encoding.UTF8.GetBytes(webhookSecret);
using var hmac = new HMACSHA256(secret);
var expectedSig = "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(requestBody))).ToLower();
var receivedSig = request.Headers["X-Waymark-Signature"];
if (!CryptographicOperations.FixedTimeEquals(
    Encoding.UTF8.GetBytes(expectedSig),
    Encoding.UTF8.GetBytes(receivedSig)))
{
    return Unauthorized();
}
```

**Validation example (Node.js)**:
```javascript
const expectedSig = 'sha256=' + createHmac('sha256', secret).update(rawBody).digest('hex');
if (!timingSafeEqual(Buffer.from(expectedSig), Buffer.from(receivedSig))) {
  return res.status(401).send('Invalid signature');
}
```

---

### Webhook deliveries stuck in "Pending" / not retrying

**Cause**: The retry background job is not running, or all retry attempts have been exhausted.

**Resolution**:
1. Check maximum retry attempts — deliveries that have exceeded the limit are marked `Failed`
2. Manual retry: `POST /webhooks/deliveries/{id}/retry`
3. Ensure the `SessionTimeoutService` and webhook retry tasks are not competing for CPU

---

## File Upload Issues

### Document upload or download returns HTTP 403

**Cause**: Both document endpoints are scoped to the session named in the route. A token for one
session cannot upload to or read from another, and a session that has reached `Completed` or
`Abandoned` accepts no further uploads.

**Resolution**:
- Confirm the `sessionId` in the path is the one the token was issued for.
- Check the session status. A finished journey must be restarted, not resumed.
- Operators reach every session's documents; an applicant token reaches exactly one.

---

### Document download returns HTTP 404 for a file that exists

**Cause**: The download scopes the *lookup*, not just the caller. A `fileId` resolves only when an
upload recorded against that session and node carries it, so a file belonging to another session is
reported as missing rather than refused.

This is deliberate: returning `403` would confirm the id names a real document.

**Resolution**: Fetch the file through the session and node it was uploaded to. The `fileId` values
for a session appear in that session's submissions (`GET /api/workflow/sessions/{sessionId}/submissions`).

---

### POST /sessions/{id}/steps/{nodeId}/documents returns HTTP 422: "File failed virus scan"

**Cause**: The uploaded file was flagged by the virus scanner.

**Resolution**: Upload a clean file. If this is a false positive in development, verify the virus scanner configuration.

---

### File upload returns HTTP 413 Request Entity Too Large

**Cause**: The file exceeds the maximum upload size.

**Default limit**: 50MB (configurable via `MaxUploadSizeMb` in config, or `MultipartBodyLengthLimit` in Kestrel config).

---

## SSE (Server-Sent Events) Issues

### Browser not receiving SSE events after submission

1. Verify the browser is connected: `GET /api/workflow/sessions/{sessionId}/events` should return
   `Content-Type: text/event-stream`.
2. **Check whether the API runs more than one replica.** This is the most common cause and it fails
   silently: an SSE stream is pinned to the instance that accepted it, so an event raised while
   handling a request on a *different* instance never reaches it. The stream stays open and simply
   delivers nothing. Set `SessionEvents__Transport=rabbitmq` (see
   [the runbook](./runbook.md#horizontal-scaling-multiple-api-instances)) so events fan out to every instance. A
   non-Development environment running the in-memory emitter logs a startup warning naming this.
3. Check for proxy/CDN timeout — many proxies time out idle SSE connections after 60s. Configure
   keep-alive or use a proxy that supports SSE.
4. Check for CORS issues if the frontend and API are on different origins — SSE requires the same
   CORS headers as regular requests.

---

### SSE connection drops after every event

**Cause**: A proxy or CDN is buffering the response and closing it after each flush.

**Resolution**: 
- Configure nginx: `proxy_buffering off; proxy_read_timeout 3600s;`
- Configure Azure App Gateway / AWS ALB: ensure HTTP/2 or long-polling is enabled

---

### SSE reconnects loop endlessly

**Cause**: The `EventSource` client keeps reconnecting because the server is closing the connection with an error.

**Resolution**:
- Check API logs for exceptions in the `/events` endpoint
- Ensure the session ID is valid and the session has not been deleted
- Check for authentication expiry — if the credential expires while connected, the next reconnect
  returns 401, causing a loop. Handle `onerror` in the frontend to stop reconnecting on auth errors.
- The browser `EventSource` API cannot set request headers, so this endpoint also accepts the
  applicant token as an `access_token` query parameter. A 403 here means the token names a different
  session; ownership is enforced identically on both paths.

---

## Frontend Issues

### Flow builder: nodes not loading

**Cause**: `GET /flows/{id}` is failing.

**Resolution**:
1. Open browser DevTools → Network tab → check the XHR request
2. Check for CORS errors
3. Verify the API base URL in the Vite config (`/api` proxy)

---

### Step renderer showing blank form

**Cause**: The node's `jsonContent` is null, empty, or not valid JSON.

**Resolution**: Update the node's `jsonContent` to include a valid form schema:
```json
{
  "fields": [
    { "id": "fullName", "type": "text", "label": "Full Name", "required": true },
    { "id": "email", "type": "email", "label": "Email Address", "required": true }
  ]
}
```

---

## CI / GitHub Actions Failures

### backend-ci: "No such file or directory: OpenOnboarding.slnx"

**Cause**: The solution file path is incorrect or the file does not exist.

**Resolution**: Verify `src/backend/OpenOnboarding.slnx` exists. The solution file must include all projects.

---

### pact-provider: "Pact file not found"

**Cause**: The `pact-consumer` job failed or the artifact was not uploaded.

**Resolution**:
1. Check the `pact-consumer` job logs for failures
2. Verify `src/frontend/pacts/` contains the generated pact JSON file after `npm run test:pact`

---

### e2e-verification: "Unable to connect to database"

**Cause**: The PostgreSQL service container is not ready when the E2E tests start.

**Resolution**: The PostgreSQL service in CI has health check options configured — it should wait automatically. If failing, check the health check configuration in `ci.yml`.

---

### Trivy scan: "HIGH or CRITICAL vulnerabilities found"

**Cause**: The Docker image contains packages with known CVEs.

**Resolution**:
1. Check the Trivy scan report in the workflow artifacts
2. Update the base image: `mcr.microsoft.com/dotnet/aspnet:10.0` — use the latest patch
3. Update NuGet packages: `dotnet outdated --upgrade` (install `dotnet-outdated-tool` globally)
4. If a vulnerability is a false positive, add an exception in `.trivyignore`

---

## Getting More Help

- Open an issue: [GitHub Issues](https://github.com/MaximumTrainer/open-onboarding/issues)
- Architecture reference: [docs/architecture/](architecture/README.md)
- Operations runbook: [docs/runbook.md](runbook.md)
