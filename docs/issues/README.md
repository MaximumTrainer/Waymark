# Waymark — Issue Backlog

This directory is a staging area for backlog items written as markdown, which a GitHub Actions
workflow turns into real GitHub issues. It is **not** a record of open work: once an item becomes a
GitHub issue, the issue is the source of truth and the file has done its job.

**The backlog is currently empty.** Every item that passed through here has been implemented and its
GitHub issue closed, so the files were removed. Open work lives on the
[issue tracker](https://github.com/MaximumTrainer/Waymark/issues).

## Adding a backlog item

Create `docs/issues/NN-short-slug.md` with YAML front-matter carrying the title and labels, followed
by the issue body in markdown:

```markdown
---
title: A one-line statement of the defect or gap
labels: bug, backend, security
---

## Summary
...

## Requirements
...

## Acceptance Criteria

- [ ] ...
```

Number files sequentially from the highest already used. Numbers 01–16 are spent.

## Creating the issues

Issues are created via the **Create Backlog Issues** GitHub Actions workflow.

1. Navigate to **Actions → Create Backlog Issues** in the repository.
2. Click **Run workflow**.
3. Optionally enable **Dry run** to preview what would be created without touching the issue tracker.
4. Click **Run workflow** to confirm.

The workflow is idempotent: it checks for an existing issue with the same title (open or closed)
before creating a new one, so re-running it is safe. It also fires automatically whenever a numbered
issue markdown file (`docs/issues/[0-9]*.md`) is pushed to `main`, and exits cleanly when there are
none.

## After an item is delivered

Delete the markdown file. Leaving it behind creates a second, stale copy of requirements that no
longer match the code, and a reader cannot tell which items are outstanding. The closed GitHub issue
and the commits that reference it are the durable record.

## History

Two sweeps have been run through this directory, both fully delivered:

| Sweep | Files | GitHub issues | Subject |
|-------|-------|---------------|---------|
| First backlog sweep | 01–10 | [#76–#86](https://github.com/MaximumTrainer/Waymark/issues?q=is%3Aissue+is%3Aclosed+76..86) | SAML wire format, service test gaps, virus-scan observability, broker resilience, storage scan defects, event emitter cleanup, frontend tests and error boundary, webhook cancellation, cloud blob storage |
| September 2026 gap review | 11–16 | [#90–#95](https://github.com/MaximumTrainer/Waymark/issues?q=is%3Aissue+is%3Aclosed+90..95) | Browser-held API key, operator UI on the public route, frontend tests absent from CI, single-instance SSE, SAML assertion encryption, analytics durability |

A follow-up review of that second sweep produced [#99–#102](https://github.com/MaximumTrainer/Waymark/issues?q=is%3Aissue+is%3Aclosed+99..102)
and [#104](https://github.com/MaximumTrainer/Waymark/issues/104), which were raised directly on the
tracker rather than through this directory.
