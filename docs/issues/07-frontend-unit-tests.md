---
title: "Frontend — add unit tests for StepRenderer, useOnboarding, and App"
labels: ["testing", "frontend"]
---

## Summary

The frontend test suite currently covers analytics helpers, the session event source hook, the API client, Pact consumer contracts, the dev proxy, and the flow authoring panel — a total of **7 test files**.

The following high-complexity components have **no unit tests at all**:

| File | LOC | Description |
|------|-----|-------------|
| `src/onboarding/components/StepRenderer.tsx` | ~512 | Renders all node types; owns `FormErrorBoundary`; calls analytics |
| `src/onboarding/hooks/useOnboarding.ts` | ~134 | Manages session lifecycle, step navigation, and SSE subscription |
| `src/App.tsx` | ~392 | Top-level routing, auth state, journey/persona selection |
| `src/onboarding/hooks/useFlow.ts` | — | Fetches flow definition and manages loading/error state |

These are the critical paths for the entire user-facing onboarding journey, and any regression here will not be caught by automated tests.

**Affected files:**
- `src/frontend/src/onboarding/components/StepRenderer.tsx`
- `src/frontend/src/onboarding/hooks/useOnboarding.ts`
- `src/frontend/src/App.tsx`
- `src/frontend/src/onboarding/hooks/useFlow.ts`

---

## Requirements

### 1 — StepRenderer tests (`stepRenderer.test.ts`)

Use `@testing-library/react` and `vitest`.

| Test | Description |
|------|-------------|
| Renders `Form` node | Given a node of type `Form` with two fields, all field labels and inputs are present in the DOM |
| Renders `Information` node | Node content text is visible |
| Renders `DocumentUpload` node | File input is rendered |
| Renders `Redirect` node | Redirect message / indicator is shown |
| `FormErrorBoundary` catches render errors | When a child throws, the error boundary message "This step could not be loaded" is shown |
| Analytics: `step_view` fired on mount | `useJourneyAnalytics().track` is called with `{ event: 'step_view', nodeType: 'Form' }` on mount |
| Validation error shown | When the parent passes a compliance error, the error message is visible |

### 2 — `useOnboarding` tests (`useOnboarding.test.ts`)

Use `renderHook` from `@testing-library/react`.

| Test | Description |
|------|-------------|
| Initial state | `currentNode` is null; `isLoading` is false; `sessionId` is null |
| `startSession` — success | After calling `startSession`, `sessionId` is set and `currentNode` is populated |
| `startSession` — API error | `error` state is set; `isLoading` returns to false |
| `submitStep` — success | `currentNode` advances to next node |
| `submitStep` — compliance violation | `complianceErrors` is populated; `currentNode` does not change |
| Session completed | When SSE emits `session-completed`, `isCompleted` becomes true |

### 3 — `useFlow` tests (`useFlow.test.ts`)

| Test | Description |
|------|-------------|
| Loading state | `isLoading` is true while fetch is in-flight |
| Success | `flow` is populated after fetch resolves |
| Error | `error` is set when fetch rejects |

### 4 — App smoke tests (`App.test.tsx`)

| Test | Description |
|------|-------------|
| Default render | App renders without crashing (smoke test) |
| Journey selector visible | Journey dropdown/list is present |
| Unauthenticated admin route | Dashboard is not shown when admin session cookie is absent |

---

## Acceptance Criteria

- [ ] `stepRenderer.test.ts` exists with all 7 test cases green.
- [ ] `useOnboarding.test.ts` exists with all 6 test cases green.
- [ ] `useFlow.test.ts` exists with all 3 test cases green.
- [ ] `App.test.tsx` exists with all 3 test cases green.
- [ ] `npm run test` in `src/frontend` passes with all new tests included.
- [ ] No existing tests are modified or removed.
- [ ] No `@ts-ignore` or `any` casts introduced solely to satisfy the test harness.
