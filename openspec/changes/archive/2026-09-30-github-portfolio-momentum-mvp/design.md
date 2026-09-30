# Design: GitHub portfolio momentum MVP

## Implementation boundary

Repository argoscope. Create src/Argoscope.Domain, src/Argoscope.Application, src/Argoscope.Infrastructure, src/Argoscope.GitHub, src/Argoscope.Api, tests/Argoscope.UnitTests, tests/Argoscope.IntegrationTests, and web/. Domain owns portfolio, repo membership, observation and score models. Application owns collection, aggregation and ranking. GitHub adapter owns REST/GraphQL, pagination and rate limit mapping. Infrastructure owns EF Core/PostgreSQL and Hangfire. API/web are read/write surfaces for Argoscope configuration only; do not mutate GitHub repositories.

## Language/runtime and conventions

C# / ASP.NET Core 10 / .NET 10, React + TypeScript / Node 20+, PostgreSQL, Hangfire, Docker. Use UTC, async/cancellation-aware I/O, EF migrations, explicit API DTOs and injected clock/provider. Planned commands: dotnet format --verify-no-changes, dotnet build, dotnet test, npm ci, npm run build, npm test, openspec validate --all --strict --no-interactive, and Workspace Governance check. No solution or package files exist yet.

## Ownership and shared code

Argoscope owns GitHub metrics, cohort membership, calculations, factor normalization and lifecycle semantics. Evaluate published dotnet-platform-libs persistence/job packages before adoption; add package references only after compatibility evidence. Keep GitHub provider and score contracts local; do not edit shared repos or vendor their source. GitHub SDK types must not leak beyond Argoscope.GitHub.

## Behavioral model and data

| Model | Contract/invariant |
|---|---|
| Portfolio | Id, name, created/updated UTC. One owner/self-hosted account in MVP. |
| Repository | Stable GitHub node id, current owner/name locator, visibility observation, created date, language summary, last activity, source freshness/status. Rename/transfer updates locator without creating a second identity. |
| Membership | PortfolioId, RepositoryId, role owned or competitor, optional category, owner-defined lifecycle label. Role/lifecycle changes are audited. |
| MetricSnapshot | RepositoryId, metric name/value, metric period/date, collected UTC, provider/source, completeness, permission and rate-limit status. Unique repo + metric + period + provider version. Missing is not zero. |
| CollectionCheckpoint | Portfolio/repo, provider query version, cursor, last successful page, retry-after UTC and state. Resume does not duplicate snapshots. |
| ScoreConfiguration | Named factors, nonnegative weights, enabled flags and version. Sum of configured weights must be greater than zero. |
| PriorityScore | RepositoryId, normalized score 0–100, active/effective weights, per-factor values, missing factors, coverage and as-of UTC. Purely advisory. |

Daily collection is idempotent; repeated successful same-period collection updates only fields returned with complete permission. Historical values are never reconstructed from current counters. Repository observation and metric values retain origin and collection timestamp. Portfolio lifecycle is always owner-controlled.

## GitHub integration and authorization

Use IGitHubRepositoryProvider to fetch public metadata and permitted owner statistics. Self-hosted MVP uses an owner-supplied least-privilege fine-grained token configured via GitHub__Token secret. Public-only read mode works without owner scopes. Hosted OAuth is deferred. GraphQL/REST pagination is cursor-based; checkpoints store cursor but never token. Every provider response maps to normalized fields plus ProviderResultStatus and safe diagnostics.

Statuses: available, partial, stale, rate_limited, unauthorized, forbidden, not_found, unavailable, malformed. Rate limit obeys reset/Retry-After with bounded retry. Unauthorized/forbidden/not-found do not retry blindly. Partial responses update verified fields only and preserve previous values with stale/partial markers for missing fields. Exceptions returned to UI are redacted (status/code/request id; no raw headers or secret).

## Metric definitions

- Store daily point-in-time counters as observed, using GitHub source timestamp where provided and collected-at UTC otherwise.
- 7/30-day velocity = (latest complete counter − latest complete counter at or before window start) / elapsed days; response includes both endpoint dates and covered days. If either endpoint is unavailable, return null and a reason.
- Percent growth is null when baseline is zero and emits new_signal=true if the current count is positive.
- 30-day acceleration = current 30-day absolute change minus preceding 30-day absolute change; insufficient coverage returns null. It is descriptive, not causal.
- External engagement counts issues/PRs and contributor identities not owned by the repository owner; owner commits/issues/PRs are separate. Unknown author identity is unknown, not external.
- Peer median requires category membership and at least five repositories with complete comparable windows; otherwise report insufficient cohort.
- Priority factor defaults use brief weights: momentum .30, engagement .25, adoption .20, external users .15, commercial .10. MVP has direct observations for momentum/engagement; unavailable components are omitted from the denominator, reported as unavailable, and effective weights renormalized. Score includes active factor values, coverage and score-config version. No star-only total substitutes for missing data.

## API/UI contract

- POST/GET /api/v1/portfolios; POST/GET /api/v1/portfolios/{id}/repositories; DELETE /api/v1/portfolios/{id}/repositories/{repoId} removes membership only and never deletes the remote repo.
- GET /api/v1/portfolios/{id}/overview?window=7d|30d; GET /api/v1/repositories/{id}/metrics; GET /api/v1/portfolios/{id}/benchmarks; GET/PUT /api/v1/portfolios/{id}/score-configuration.
- Invalid weights/membership/role return 400 problem details. Missing portfolio/repository returns 404. Unauthorized owner-private metrics return 403 or a visible unavailable field according to whether membership can still be shown; never fabricate zeros.
- Dashboard surfaces observed-at/freshness, completeness, rate-limit state, external/owner distinction, comparator cohort count, score weights and missing factors.

## Failure and security policy

Malformed repo locator: 400, no membership. Duplicate membership: return existing association. Rate limit: persist checkpoint/retry-after, keep prior snapshot stale. Token invalid/scope missing: mark corresponding fields unauthorized/forbidden and stop retry until configuration changes. Deleted/private repos: not-found or forbidden status, last known snapshot remains with timestamp and stale warning. Database failure rolls back current page writes; checkpoint advances only in the same transaction as complete page persistence. Cancellation propagates. A failed run never wipes last good data. Metrics collection is read-only against GitHub.

## Verification oracle

Unit tests with fixed UTC snapshots assert velocity endpoints, missing endpoints, zero baseline, acceleration and coverage; engagement separates owner/external/unknown; score normalization, nonnegative weights, missing-factor renormalization and factor coverage are deterministic. Fake GitHub contract tests cover public/owner responses, pagination, checkpoint resume, rate limit, unauthorized, forbidden, not-found, malformed, timeout, partial fields and cancellation. PostgreSQL integration tests run migrations and prove daily uniqueness, atomic page/checkpoint writes and preservation of last good values. API/UI end-to-end fixtures assert rank inputs/weights/freshness/missing values and owner vs external activity. Strict OpenSpec and shared governance validation must pass.

## Decision ledger

- Self-hosted single-owner MVP and PAT are chosen; OAuth hosted accounts are deferred.
- Core MVP covers public metadata and only owner-authorized fields; unavailable owner traffic is not represented as zero.
- Daily snapshots do not backfill pre-installation history.
- Score weights follow the user brief; missing MVP factors are explicitly excluded and coverage disclosed.
- Adoption/package downloads, commercial intent, alerts, decision journal, billing, and lifecycle automation are separate follow-up packages.
