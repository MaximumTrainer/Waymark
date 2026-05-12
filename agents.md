# Agent Engineering Standards

This repository follows the standards below for all agent-driven and human-driven changes.

## 1) Architecture: Ports & Adapters (Hexagonal)

- Keep business rules inside the core domain/application layers.
- Model external dependencies (database, HTTP, messaging, files, time, etc.) as **ports** (interfaces/contracts).
- Implement technology-specific integrations as **adapters** in infrastructure or edge layers.
- Enforce dependency direction inward: adapters depend on ports; core logic must not depend on adapter implementations.
- Keep controllers/UI/API handlers thin and delegate workflow logic to application services/use-cases.

## 2) Runtime: .NET 10

- Backend projects and tests must target and run on the **.NET 10** runtime.
- New backend code must be compatible with .NET 10 APIs and tooling.
- CI/CD and local development environments should use the .NET 10 SDK/runtime for restore, build, and test.

## 3) Development Method: TDD (Red → Green → Refactor)

For every change:

1. **Red**: Add or update a failing automated test that describes the required behavior.
2. **Green**: Implement the minimal production change needed to pass the test.
3. **Refactor**: Improve design/readability while keeping tests green.

Additional expectations:

- Do not merge behavior changes without corresponding automated tests.
- Keep tests deterministic, isolated, and focused on observable behavior.
- Prefer small iterations with frequent test execution.
