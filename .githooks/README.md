# Git hooks

Versioned hooks, enabled per clone by pointing `core.hooksPath` here:

```bash
sh scripts/install-git-hooks.sh          # macOS / Linux / Git Bash
pwsh scripts/install-git-hooks.ps1       # Windows PowerShell
```

`npm ci` or `npm install` in `src/frontend` also runs the installer, so a frontend setup enables
them without a separate step.

## What runs when

| Hook | Checks | Typical cost |
|------|--------|--------------|
| `pre-commit` | Secret scan of the staged diff; `eslint` if frontend files changed; `dotnet build` if backend files changed | seconds |
| `commit-msg` | Conventional Commits subject line | instant |
| `pre-push` | `dotnet test` (Application.Tests), `npm test`, `npm run build`; refuses a direct push to `main` | ~1 minute |

Work is scoped to what the commit actually touches, so a docs-only change runs neither toolchain.

## What is deliberately not here

Suites needing PostgreSQL, RabbitMQ or a browser — Pact provider verification and Playwright — stay
in CI. A hook that only passes on a machine with the right services running is a hook people learn
to bypass, and a bypassed hook enforces nothing.

CI remains the authority. These hooks exist to catch the cheap mistakes before review, not to
replace it.

## Bypassing

```bash
SKIP_HOOKS=1 git commit ...
SKIP_HOOKS=1 git push ...
```

Preferred over `--no-verify` because it is visible in the command that used it, and it works for
`commit-msg`, which `--no-verify` also skips.

`ALLOW_PUSH_TO_MAIN=1` overrides the `main` branch guard specifically.

## The secret scan

Only lines the commit *adds* are scanned, for:

- PEM private key blocks.
- `VITE_*` variables holding a key, secret, token or password. Vite inlines these into the browser
  bundle as literal strings, so a secret named this way is readable by every visitor — the defect
  fixed in #90.
- Non-empty `SigningKey`, `SpPrivateKey`, `ClientSecret`, `Secret`, `Password` or `ApiKey` values in
  configuration. Recognisable local placeholders (`dev-`, `test-`, `example`, `change-in-production`)
  are allowed so committed development defaults stay editable.

It is a backstop for the obvious cases, not a substitute for secret scanning on the remote.
