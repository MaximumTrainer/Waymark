# Getting Started

This guide is for engineers and SDETs onboarding to the repository.

## 1. Prerequisites

- .NET SDK 10.x
- Node.js 22.x
- Docker (for local PostgreSQL)

## 2. Clone and inspect

```bash
git clone <repo-url>
cd open-onboarding
```

## 3. Start local dependencies

```bash
docker compose up -d
```

Local PostgreSQL from `docker-compose.yml`:
- host: `localhost`
- port: `5432`
- database: `onboarding`
- user/password: `postgres` / `postgres`

## 4. Start backend API

Use a connection string that matches the local compose database:

```bash
ConnectionStrings__OnboardingDb="Host=localhost;Port=5432;Database=onboarding;Username=postgres;Password=postgres" \
  dotnet run --project src/backend/OpenOnboarding.Api
```

Verification:
- Swagger UI in Development: `https://localhost:7000/swagger`
- Auth diagnostics endpoint: `GET http://localhost:5072/api/auth/me`

## 5. Start frontend

```bash
cd src/frontend
cp .env.example .env.local
npm install
npm run dev
```

Recommended `.env.local` values:
- `VITE_API_BASE_URL=http://localhost:5072`
- `VITE_API_KEY=dev-api-key-change-in-production`

## 6. Run validation commands

### Frontend

```bash
cd src/frontend
npm ci
npm run lint
npm run build
npm run test
npm run test:pact
```

### Backend

```bash
dotnet restore src/backend/OpenOnboarding.slnx
dotnet build src/backend/OpenOnboarding.slnx --no-restore -c Release
dotnet test src/backend/OpenOnboarding.slnx -c Release
```

## 7. Contribution workflow

1. Create a feature/fix branch.
2. Add or update tests first for all code changes (TDD: Red → Green → Refactor).
3. Keep API/controller changes thin; place workflow logic in application/infrastructure services.
4. Run backend + frontend validation commands before pushing.
5. Open a PR and ensure CI passes.
6. Do not merge behavior changes unless corresponding automated tests are included and passing.
