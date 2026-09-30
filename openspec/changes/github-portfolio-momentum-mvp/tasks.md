# Tasks: GitHub portfolio momentum MVP

## 1. BFS — Baseline and impact coverage

- [ ] B1 Map requirements R1–R6 to Domain/Application/GitHub/Infrastructure/API/web, storage, scopes and failure cases; confirm no GitHub mutation endpoints.
- [ ] B2 Create fixed-UTC metric/score fixtures and fake GitHub provider contracts for pagination, partial fields, missing permissions and rate limits.
- [ ] B3 Inspect dotnet-platform-libs package manifest/release versions for persistence and Hangfire adoption; document compatible/local decision without editing sibling.
- [ ] B4 Confirm stable GitHub node identity, owner-vs-external attribution policy, snapshot uniqueness and implementation-handoff gate.

## 2. DFS — Requirement-by-requirement implementation

- [ ] D1 (R1) Implement portfolio, repository identity and owned/competitor membership/lifecycle CRUD with audit and validation.
- [ ] D2 (R2) Implement PAT secret config and IGitHubRepositoryProvider GraphQL/REST adapter with pagination, scopes and normalized result statuses.
- [ ] D3 (R2) Implement daily snapshot schema/migration, collection job, idempotency, atomic page/checkpoint persistence, rate limits and last-good retention.
- [ ] D4 (R3) Implement pure 7/30-day velocity and 30-day acceleration with null/zero/missing-window behavior and UTC-boundary tests.
- [ ] D5 (R4) Implement external/owner/unknown engagement series and verified author attribution.
- [ ] D6 (R5) Implement competitor comparisons, category medians only for eligible cohorts, and freshness/coverage output.
- [ ] D7 (R6) Implement configurable factor weights, missing-factor normalization, score coverage and read-only portfolio dashboard.

## 3. BFS — Cross-surface regression and completeness

- [ ] C1 Recheck renamed/transferred/private/deleted repos, partial pages, API retries, rate-limit resets, duplicate runs and concurrent collection.
- [ ] C2 Recheck aggregation, comparator cohorts and score views for stale/missing/zero data; prove no mutation requests can reach GitHub.
- [ ] C3 Review all API/UI labels for as-of time, freshness, permissions, external vs owner activity, effective weights and unavailable factors.
- [ ] C4 Reconcile R1–R6 with storage, integrations, callers, docs and tests; remove scaffolding placeholders.

## 4. Verification

- [ ] V1 Run dotnet format --verify-no-changes, dotnet build, dotnet test, npm ci, npm run build, and npm test; capture results.
- [ ] V2 Run strict OpenSpec validation and python3 scripts/workspace_check.py --root .. --project argoscope; resolve blockers.
- [ ] V3 Run against synthetic portfolio snapshots; verify UI and capture a privacy-reviewed screenshot. No live GitHub call is required for unit/integration fixtures.
- [ ] V4 Archive completed change, commit implementation/tests/archive/generated specs, then commit only HANDOFF.md pointer/evidence; stop after exactly two commits.
