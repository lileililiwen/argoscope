# Argoscope architecture decisions

Status: implemented baseline; each ADR below is backed by shipped code and tests.

## ADR 0001 — .NET 10 + React self-hosted analytics app

- **Decision:** ASP.NET Core 10 API, React + TypeScript web client, PostgreSQL, Hangfire and Docker. Solution boundaries: Argoscope.Domain, Argoscope.Application, Argoscope.Infrastructure, Argoscope.GitHub, Argoscope.Api, tests/, and web/.
- **Reason:** scheduled API collection, time-series persistence, deterministic aggregation and a portfolio dashboard fit the specified stack and ownership.
- **Consequence:** self-hosted single owner first; PAT secret comes from environment/secret store. Hosted multi-tenant access ships on top via tenant isolation, roles and migration gating (ADR 0004).

## ADR 0004 — Hosted operations readiness

- **Decision:** split health gates (`/health/live` dependency-free, `/health/ready` checking database/migrations/jobs with status-only responses), immutable revision-tagged container artifact (`ARGOSCOPE_RELEASE_REVISION` → `/ops/release`), expand-then-contract migrations under a single lock, encrypted backups with isolated restore rehearsals measured against RPO 24 h / RTO 8 h, and an auditable tenant-deletion lifecycle (tombstone → 30-day active purge → backup expiry) plus incident evidence. Provider and region selection stay explicit operator decisions; no vendor assumption is encoded.
- **Reason:** hosted identity and billing do not prove the service can be deployed, observed, recovered and supported; launch needs measured rehearsal evidence, not build success.
- **Consequence:** release promotion is blocked when migration, readiness, backup verification or restore objectives fail; missed rehearsals open remediation incidents; launch requires named operator sign-off. See `docs/operations/` and `scripts/ops_readiness.py`.

## ADR 0002 — Provider-backed, revision-bound snapshots

IGitHubRepositoryProvider owns REST/GraphQL calls, pagination, permission observations and rate-limit classification. SnapshotRepository stores one daily observation per repository/metric date with raw source timestamp, collected-at UTC, metric value, completeness and provider status. Never backfill historical values from present-day counters. Partial provider results mark individual fields incomplete rather than replacing known values with zero.

Repo identity is GitHub's stable node id, with owner/name as a display locator that can change after rename/transfer. Portfolio membership records owned/competitor role and category. A private repo can only be added/read after owner authorization; missing permission is unavailable, not an empty or zero-valued repository.

## ADR 0003 — Deterministic analytics with coverage-aware ranks

For daily snapshots at UTC boundaries, velocity is (latest_count − count_at_or_before_window_start) / elapsed_days, with dates and sample coverage returned. 7/30-day growth percentage is null when baseline is zero; label a new repo separately. Acceleration is current 30-day absolute change minus preceding 30-day absolute change; do not divide by zero or call it a causal signal. Engagement counts external issue/PR/contributor activity separately from owner-authored actions.

Priority score uses configured component weights (defaults from product brief: momentum .30, engagement .25, adoption .20, external users .15, commercial .10). Normalize available factors to a declared 0–100 portfolio percentile; if a component lacks MVP data, omit it from both numerator and denominator, report effective weights and coverage, and lower confidence. V1 can rank on available momentum/engagement while disclosing unavailable adoption/commercial factors.

## Shared-library decision

dotnet-platform-libs owns .NET 10 web composition, EF Core/PostgreSQL and Hangfire adapters. Decision: **evaluated, local adapters retained** (see `docs/architecture/platform-evaluation.md`). The sibling surfaces are local-only 0.1.0 and not on the pinned nuget.org feed; tenant membership/authorization, billing entitlement and operations policies are product-specific. Check package manifest/release versions, cross-project contracts and migration compatibility before any future adoption. Argoscope owns GitHub GraphQL/REST semantics, snapshot schema, time series and scoring. No source is copied and no sibling is edited.

## Security and failure behavior

Use least-privilege user-supplied GitHub token; store only in deployment secret storage and redact logs. Respect API rate limits and Retry-After; persist resumable pagination checkpoints. Unauthorized/forbidden/deleted/private repos are explicit statuses. Exhausted transient requests leave previous snapshots intact and mark them stale; partial successes update only verified metrics and record omissions. No analytics action mutates a GitHub repository.
