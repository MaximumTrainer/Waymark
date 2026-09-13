# Getting Started

This guide is for engineers and SDETs onboarding to the repository.

## 1. Prerequisites

- .NET SDK 10.x
- Node.js 22.x
- Docker (for local PostgreSQL, and optionally RabbitMQ)

## 2. Clone and inspect

```bash
git clone <repo-url>
cd open-onboarding
```

## 3. Start local dependencies

```bash
docker compose up -d postgres
```

Local PostgreSQL from `docker-compose.yml`:
- host: `localhost`
- port: `5432`
- database: `onboarding`
- user/password: `postgres` / `postgres`

`docker compose up -d` with no service name also builds and starts the `api` container, which you do
not want while running the API from source. Name the service you need.

### RabbitMQ (optional)

```bash
docker compose up -d rabbitmq
```

Only needed for two things: running more than one API replica, where a broker carries session
events between them (`SessionEvents__Transport=rabbitmq`), and running the broker-backed tests,
which skip themselves when nothing is listening. Management UI on `http://localhost:15672`
(`guest`/`guest`).

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

Only one value is needed:
- `VITE_API_BASE_URL=http://localhost:5072`

**Do not put an API key in `.env.local`.** Vite inlines every `VITE_*` variable into the built
bundle as a literal string, and `Authentication:ApiKey` maps to a full Operator principal. The app
needs no credential of its own: the onboarding journey starts anonymously and is handed a token
scoped to that session, and operator screens use the SSO cookie. See
[API credentials](./docs/runbook.md#api-credentials).

Frontend dev server: `http://localhost:5173`

Available routes:
- `/` — the applicant onboarding journey. `/?flowId=<id>` selects a specific journey.
- `/admin` — operator console: flow authoring, version history, analytics, sessions, webhook deliveries (requires Operator SSO)
- `/admin/journey-builder` — Visual Journey Builder admin UI (requires Operator SSO — see [`user-guide.md`](./user-guide.md) section 10 for usage)

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
dotnet test src/backend/OpenOnboarding.slnx -c Release --filter "FullyQualifiedName!~OpenOnboarding.Pact.Tests.E2E"
```

The filter excludes the end-to-end suite, which needs a running PostgreSQL and is what CI runs it
against. Without it, `dotnet test` fails on a machine with no database.

The main suite runs against an in-memory database and mocked transports, so it needs nothing
installed. A few tests are backed by a real RabbitMQ broker and **skip with a clear reason** when
none is reachable — start one as in section 3 to run them:

```bash
docker compose up -d rabbitmq
dotnet test src/backend/OpenOnboarding.Application.Tests
```

Point them at a different broker with `SESSIONEVENTS__RABBITMQ__URI`. CI sets
`REQUIRE_BROKER_TESTS=1`, which turns a missing broker into a failure rather than a skip, so a
service-container problem cannot pass as a green build.

## 7. Install the git hooks

```bash
sh scripts/install-git-hooks.sh          # macOS / Linux / Git Bash
pwsh scripts/install-git-hooks.ps1       # Windows PowerShell
```

`npm install` in `src/frontend` runs this for you, so step 5 already covers it.

The hooks live in [`.githooks/`](./.githooks/README.md) and are enabled per clone via
`core.hooksPath`:

- **pre-commit** — scans the staged diff for secrets, then lints or builds only the areas you
  touched.
- **commit-msg** — requires a Conventional Commits subject (`feat:`, `fix:`, `docs:`, …).
- **pre-push** — runs the backend and frontend test suites, and refuses a direct push to `main`.

Bypass once with `SKIP_HOOKS=1` when you need to. CI remains the authority; the hooks exist to
catch the cheap mistakes before review.

## 8. Contribution workflow

1. Create a feature/fix branch.
2. Add or update tests first for all code changes (TDD: Red → Green → Refactor).
3. Keep API/controller changes thin; place workflow logic in application/infrastructure services.
4. Run backend + frontend validation commands before pushing. The pre-push hook runs the
   dependency-free suites for you; the Pact and Playwright suites need PostgreSQL and a browser and
   run in CI.
5. Open a PR and ensure CI passes.
6. Do not merge behavior changes unless corresponding automated tests are included and passing.
7. For documentation-only changes, validate command examples against a local setup and ensure CI references remain accurate.
