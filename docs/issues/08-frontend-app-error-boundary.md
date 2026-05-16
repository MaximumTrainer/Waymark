---
title: "Frontend — add top-level error boundary for auth/routing failures"
labels: ["reliability", "frontend"]
---

## Summary

`StepRenderer.tsx` correctly wraps its form in a `FormErrorBoundary` that catches rendering errors within a single step. However, `App.tsx` — which controls authentication state, journey selection, persona routing, and admin-dashboard rendering — has **no error boundary** at all.

An unhandled JavaScript exception in any top-level component (e.g. a failed auth check, a missing flow definition, an unexpected null during persona resolution) will crash the entire application to a blank page with no user-facing feedback.

**Affected file:** `src/frontend/src/App.tsx`

---

## Requirements

### 1 — Top-level `AppErrorBoundary` component

Create a new class component `AppErrorBoundary` (or a file `src/components/AppErrorBoundary.tsx`):

- Catches any render-phase error originating from `<App />` or its descendants.
- Displays a user-friendly error screen:
  ```
  Something went wrong. Please refresh the page or contact support.
  [Refresh] button
  ```
- Logs the error to `console.error` in development.
- Exposes the caught error detail only in development (hide stack traces in production).
- Accepts an optional `onReset` prop so that tests can programmatically reset the boundary.

### 2 — Wire it up in `main.tsx`

Wrap the root render in `AppErrorBoundary`:

```tsx
// src/frontend/src/main.tsx
root.render(
  <AppErrorBoundary>
    <App />
  </AppErrorBoundary>
)
```

### 3 — Tests

Add `AppErrorBoundary.test.tsx`:

| Test | Expected outcome |
|------|-----------------|
| Child throws during render | Error boundary message is displayed; child content is not |
| Error message is shown | "Something went wrong" text is visible |
| Refresh button present | A button that calls `window.location.reload` (or `onReset`) is present |
| Error detail hidden in production | Stack trace is not present in the DOM when `import.meta.env.PROD === true` |

---

## Acceptance Criteria

- [ ] `AppErrorBoundary` component exists and is rendered as the root wrapper in `main.tsx`.
- [ ] An unhandled render error in any child of `<App />` shows the friendly error screen instead of a blank page.
- [ ] Stack trace / error detail is not exposed in production builds.
- [ ] All 4 `AppErrorBoundary.test.tsx` tests pass.
- [ ] `npm run test` passes.
- [ ] `npm run build` produces no TypeScript errors.
