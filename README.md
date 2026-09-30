# Argoscope

**An open-source portfolio analytics dashboard for GitHub projects.** Argoscope helps maintainers decide which owned projects and competitors deserve attention by tracking momentum, external engagement, and comparable trends.

## Product boundary

Argoscope analyzes portfolios of repositories, not isolated star counters. Owners add repositories and competitors, collect timestamped GitHub metrics, compare growth against category peers, and rank attention using a transparent configurable formula. Metrics and rankings expose their inputs and data coverage.

Deployment has two profiles: self-hosted single-owner (open local behavior, owner-supplied GitHub token for private analytics) and hosted multi-tenant (tenant-isolated OIDC access, provider-backed billing, readiness-gated operations). See `docs/product-brief.md` and `ROADMAP.md`.

## Implemented

- Portfolio and repository/competitor membership with lifecycle labels.
- Daily GitHub snapshots (stars, forks, watchers, issues, PRs, contributors, releases, commits, languages, age, last activity) with source, observed-at time and partial-failure evidence.
- 7/30-day velocity and 30-day acceleration; engagement separates owner from external activity.
- Competitor comparisons, category medians and a transparent configurable priority score with coverage shown.
- Package adoption (Docker Hub, npm, PyPI, crates.io), commercial-signal review queue, decision journal and attention alerts.
- Hosted identity (tenant isolation, Owner/Editor/Viewer roles, invites, OIDC, migration) and hosted billing (HMAC webhooks, entitlements, 402 enforcement).
- Hosted operations: split health gates (`/api/v1/health/live`, `/api/v1/health/ready`), immutable revision-tagged release (`/api/v1/ops/release`), encrypted-backup/isolated-restore rehearsals, auditable tenant deletion and incident evidence. Runbooks live in `docs/operations/`.

## Deferred

Traffic/views/clones (owner permission-bound) and automatic lifecycle transitions. Everything else in the original deferred list — package downloads, commercial-intent review, alerts, decision journal, hosted accounts, billing, SaaS operations — is implemented and tested.

## Development

- Stack: ASP.NET Core 10, React + TypeScript, PostgreSQL (InMemory for local/test), GitHub GraphQL/REST APIs, Docker.
- Secrets (`GitHub:Token`, `Identity:Oidc:*`, `Billing:WebhookSecret`) come from deployment secret storage only; never commit or log them.
- Verify: `dotnet build Argoscope.sln`, `dotnet test Argoscope.sln`, `npm run build --prefix web`, `npm test --prefix web`, `openspec validate --all --strict --no-interactive`, `python3 scripts/verify_bootstrap.py`.
- Release gate: `docker build -t argoscope:ci .`, then `python3 scripts/ops_readiness.py --base-url <candidate>` blocks promotion when readiness fails. Full CI (build, tests, container, gate) runs in `.github/workflows/validation.yml`.

## License

MIT. See LICENSE.
