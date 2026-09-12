---
title: "npm test fails on a clean checkout and frontend unit tests never run in CI"
labels: ["bug", "testing", "frontend"]
---

## Summary

Two defects compound each other:

1. `npm test` (vitest) **fails** on a clean checkout — it collects the Playwright specs in `e2e/` and every one of them errors.
2. The `frontend-ci` job never runs `npm test`, so the failure is invisible and the 108 frontend unit tests added for the frontend-unit-tests issue are **not enforced on any pull request**.

**Affected files:**
- `src/frontend/vitest.config.ts`
- `.github/workflows/ci.yml` (`frontend-ci` job, lines 93–120)
- `src/frontend/playwright/onboarding-journeys.spec.ts` (orphaned duplicate)

### Reproduction

```
$ cd src/frontend && npm test
 Test Files  6 failed | 14 passed (20)
      Tests  108 passed (108)
```

Each failure is the same:

```
Error: Playwright Test did not expect test.describe() to be called here.
 ❯ e2e/step-renderer.spec.ts:41:6
```

### Root cause

`vitest.config.ts` excludes the wrong directory:

```ts
exclude: [...configDefaults.exclude, 'playwright/**'],
```

The Playwright specs live in `e2e/` — `playwright.config.ts` sets `testDir: './e2e'`. The `playwright/` directory still contains `onboarding-journeys.spec.ts`, byte-identical to `e2e/onboarding-journeys.spec.ts`, and is outside `testDir`, so that copy is never executed by Playwright either. Vitest excludes the dead directory and collects the live one.

### Why CI did not catch it

`frontend-ci` runs `npm ci` → `npm run lint` → `npm run build`. There is no test step. The other jobs run `npm run test:pact` and `npm run test:e2e`, so Pact and Playwright are covered, but the vitest suite is not.

---

## Requirements

1. Fix the vitest exclude so it matches the real Playwright directory (`e2e/**`), or scope vitest's `include` to `src/**`.
2. Delete the orphaned `src/frontend/playwright/` directory — it is a stale duplicate that no runner executes.
3. Add a test step to `frontend-ci` so a failing unit test blocks the pull request.
4. Consider uploading vitest coverage as a build artifact, matching the backend job's behaviour.

---

## Acceptance Criteria

- [ ] `npm test` exits `0` on a clean checkout of `main`.
- [ ] `npm test` reports `20 passed (20)` test files — no Playwright spec is collected by vitest.
- [ ] `src/frontend/playwright/` no longer exists, and `npm run test:e2e` still runs the full `e2e/` suite.
- [ ] `frontend-ci` runs `npm test` and the job fails when a unit test fails (demonstrate with a deliberately broken test on a scratch branch).
- [ ] Introducing a failing vitest assertion causes a red CI check on a pull request.
