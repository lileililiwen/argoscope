# Tasks: Package adoption metrics

## 1. BFS — Baseline and impact coverage
- [x] B1 Map R1–R4 across Core/Application/Infrastructure/API/Web, persistence and daily job.
- [x] B2 Define provider fixtures for zero, missing, stale, rate limit, deletion and incompatible units.
- [x] B3 Confirm package-coordinate ownership and secret handling; inspect shared package compatibility without editing sibling.
- [x] B4 Confirm exact unit/window contract and implementer handoff.

## 2. DFS — Requirement-by-requirement implementation
- [x] D1 (R1) Add explicit package associations and provider-coordinate validation.
- [x] D2 (R2) Implement provider contracts/adapters for Docker Hub, npm, NuGet, PyPI and crates.io using documented interfaces.
- [x] D3 (R3) Persist idempotent interval observations and provider status; preserve last-good data on failure.
- [x] D4 (R4) Add read API and adoption series UI with unit, window, freshness and coverage disclosure.

## 3. BFS — Cross-surface regression and completeness
- [x] C1 Verify rate-limit, delete/rename, duplicate jobs, empty-vs-zero and unit separation.
- [x] C2 Verify no adoption counts are mislabeled or fed into ranking without explicit factor configuration.
- [x] C3 Reconcile R1–R4 across migration, job, endpoints, UI, tests and docs.

## 4. Verification
- [x] V1 Run format/build/tests for .NET and frontend plus strict OpenSpec validation.
- [x] V2 Assert deterministic provider fixture readback and last-good behavior; no live credentials required.
