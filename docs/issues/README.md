# Waymark — Issue Backlog

This directory contains the detailed requirements for identified functional gaps, TODOs, and stub implementations in the codebase. Each file is formatted as a GitHub issue and can be bulk-created using the helper script below.

## Creating the issues

Issues are created via the **Create Backlog Issues** GitHub Actions workflow.

1. Navigate to **Actions → Create Backlog Issues** in the repository.
2. Click **Run workflow**.
3. Optionally enable **Dry run** to preview what would be created without touching the issue tracker.
4. Click **Run workflow** to confirm.

The workflow is idempotent: it checks for an existing issue with the same title (open or closed) before creating a new one, so re-running it is safe.

The workflow also fires automatically whenever a numbered issue markdown file (`docs/issues/[0-9]*.md`) is pushed to `main`.

## Issue index

| # | File | Title | Labels |
|---|------|-------|--------|
| 1 | [01-saml-placeholder-wire-format.md](01-saml-placeholder-wire-format.md) | Replace placeholder SAML wire format with standard SAML XML | bug, security, authentication |
| 2 | [02-session-timeout-service-tests.md](02-session-timeout-service-tests.md) | Add unit tests for SessionTimeoutService | testing, reliability |
| 3 | [03-null-virus-scan-observability.md](03-null-virus-scan-observability.md) | NullVirusScanService — add monitoring and observability for bypassed scans | security, observability |
| 4 | [04-rabbitmq-eventbus-resilience.md](04-rabbitmq-eventbus-resilience.md) | RabbitMqEventBus — fix sync-over-async startup registration and add resilience | bug, reliability, infrastructure |
| 5 | [05-document-storage-scan-missing-file.md](05-document-storage-scan-missing-file.md) | LocalDocumentStorageService — fix silent clean-result for missing files in ScanAsync | bug, security |
| 6 | [06-session-event-emitter-cleanup.md](06-session-event-emitter-cleanup.md) | InMemorySessionEventEmitter — document channel eviction behaviour and add stale-channel cleanup | reliability, observability |
| 7 | [07-frontend-unit-tests.md](07-frontend-unit-tests.md) | Frontend — add unit tests for StepRenderer, useOnboarding, and App | testing, frontend |
| 8 | [08-frontend-app-error-boundary.md](08-frontend-app-error-boundary.md) | Frontend — add top-level error boundary for auth/routing failures | reliability, frontend |
| 9 | [09-webhook-cancellation-token.md](09-webhook-cancellation-token.md) | WebhookService — CancellationToken not propagated through delivery retry loop | bug, reliability |
| 10 | [10-cloud-document-storage.md](10-cloud-document-storage.md) | Add cloud/blob storage adapter for document storage (production readiness) | enhancement, infrastructure |

## Priority summary

| Priority | Issues |
|----------|--------|
| 🔴 Critical | #1 (SAML placeholder is the only SSO implementation) |
| 🟠 High | #2, #5, #7, #9 (untested background service; security bug; untested frontend core; cancelled delivery not recorded) |
| 🟡 Medium | #3, #4, #6, #8 (observability; resilience; memory leak; UX safety net) |
| 🟢 Low / Enhancement | #10 (production storage — needed before cloud deploy) |
