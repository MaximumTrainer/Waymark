# Open Onboarding

Open Onboarding is a schema-driven onboarding platform with:
- A .NET 10 backend API for flow definition, session orchestration, compliance checks, document upload, Server-Sent Event progress, and webhooks
- A React + Vite frontend for rendering dynamic onboarding steps and visualizing flow paths
- CI/CD workflows for validation and cloud deployment targets (Azure and AWS)

## Documentation map

- [`getting-started.md`](./getting-started.md)
- [`user-guide.md`](./user-guide.md)
- [`feature-gaps.md`](./feature-gaps.md)

## Repository structure

```text
open-onboarding/
├── Dockerfile
├── docker-compose.yml
├── src/
│   ├── backend/
│   │   ├── OpenOnboarding.Api/
│   │   ├── OpenOnboarding.Application/
│   │   ├── OpenOnboarding.Domain/
│   │   ├── OpenOnboarding.Infrastructure/
│   │   ├── OpenOnboarding.Application.Tests/
│   │   ├── OpenOnboarding.Pact.Tests/
│   │   └── OpenOnboarding.slnx
│   └── frontend/
│       ├── src/
│       ├── package.json
│       └── vite.config.ts
└── .github/workflows/
    ├── ci.yml
    ├── cd-azure.yml
    └── cd-aws.yml
```

## Architecture

The backend uses a Ports & Adapters (Hexagonal) structure:
- **Domain**: entities and enums
- **Application**: contracts, validators, and service interfaces (ports)
- **Infrastructure**: EF Core persistence, adapters, and workflow/compliance implementations
- **API**: ASP.NET Core controllers, authN/authZ, OpenAPI, and transport concerns

The frontend is a Vite React app with:
- `StepRenderer` for schema-driven node UX (`Form`, `DocumentUpload`, `Redirect`, `Information`, `Logic`)
- `JourneyBuilder` for graph visualization via React Flow
- `useOnboarding` hook for session lifecycle and Server-Sent Event updates

## Build, test, and validation

### Backend (.NET 10)

```bash
dotnet restore src/backend/OpenOnboarding.slnx
dotnet build src/backend/OpenOnboarding.slnx --no-restore -c Release
dotnet test src/backend/OpenOnboarding.slnx -c Release
```

Notes:
- Pact provider tests require PostgreSQL to be running and reachable on `localhost:5432` (start it with `docker compose up -d` from the repository root, as shown in **Run locally**).
- API startup applies EF Core migrations and seed data automatically.

### Frontend (Node 22)

```bash
cd src/frontend
npm ci
npm run lint
npm run build
npm run test
npm run test:pact
```

## Run locally

1. Start PostgreSQL:

```bash
docker compose up -d
```

2. Run backend API:

```bash
ConnectionStrings__OnboardingDb="Host=localhost;Port=5432;Database=onboarding;Username=postgres;Password=postgres" \
  dotnet run --project src/backend/OpenOnboarding.Api
```

3. Run frontend:

```bash
cd src/frontend
npm install
npm run dev
```

Frontend dev server: `http://localhost:5173`  
Backend HTTP: `http://localhost:5072`  
Backend Swagger (Development): `https://localhost:7000/swagger`

## Deployment

GitHub Actions workflows:
- **CI (`ci.yml`)**: backend restore/build/test, frontend lint/build, pact consumer/provider verification
- **CD Azure (`cd-azure.yml`)**: backend container to Azure Container Apps, frontend to Azure Static Web Apps
- **CD AWS (`cd-aws.yml`)**: backend container to ECS/ECR, frontend static assets to S3 + CloudFront

Workflow files include required secret names for each deployment target.
