# open-onboarding

Schema-driven onboarding boilerplate for automated user journeys and compliance workflows.

## Quick start

```bash
# 1. Start PostgreSQL
docker-compose up -d

# 2. Start backend (migrations + seed data applied automatically)
dotnet run --project src/backend/OpenOnboarding.Api
# → API available at https://localhost:7000
# → Swagger UI at https://localhost:7000/swagger

# 3. Start frontend
cd src/frontend
npm install
npm run dev
# → UI available at http://localhost:5173
```

The backend automatically applies EF Core migrations and seeds the example compliance flow
on first startup in the Development environment.

## Generating new migrations

```bash
dotnet ef migrations add <Name> \
  --project src/backend/OpenOnboarding.Infrastructure \
  --startup-project src/backend/OpenOnboarding.Api
```

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
