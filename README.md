# open-onboarding

Schema-driven onboarding boilerplate for automated user journeys and compliance workflows.

## Directory layout

- `src/backend/OpenOnboarding.Domain`: Domain entities (`Flow`, `Node`, `Connection`, `Session`, `Submission`, `CustomerProfile`)
- `src/backend/OpenOnboarding.Application`: Contracts, validators, workflow service abstractions
- `src/backend/OpenOnboarding.Infrastructure`: EF Core `OnboardingDbContext` + workflow state-machine service
- `src/backend/OpenOnboarding.Api`: ASP.NET Core API with `WorkflowController`
- `src/backend/OpenOnboarding.Application.Tests`: Focused workflow transition tests
- `src/frontend`: React + TypeScript + Tailwind + Radix + React Flow starter
- `src/frontend/src/schemas/flow-definition.example.json`: Conditional branching flow definition example

## Backend endpoints

- `POST /api/workflow/sessions/start`
- `POST /api/workflow/sessions/{sessionId}/steps/{nodeId}/submit`
- `GET /api/workflow/sessions/{sessionId}/next`
