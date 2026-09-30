# Tasks: Hosted multi-user identity

## 1. BFS — Baseline and impact coverage
- [x] B1 Inventory every tenant-owned table, endpoint, job, cache, export and GitHub credential boundary; map R1–R4.
- [x] B2 Create two-tenant fixtures, legacy migration dataset, role matrix, expired/replayed invite and OIDC failure cases.
- [x] B3 Select OIDC provider/deployment config and inspect compatible published shared auth package without sibling edits.
- [x] B4 Confirm no unscoped query/fallback and migration rollback/verification contract.

## 2. DFS — Requirement-by-requirement implementation
- [x] D1 (R1) Add OIDC identity, tenant and membership lifecycle with secure session/CSRF behavior.
- [x] D2 (R2) Add tenant IDs/filters/policies to all persistence, endpoints, jobs, caches and exports.
- [x] D3 (R3) Migrate existing single-owner data transactionally and enforce non-null tenant ownership.
- [x] D4 (R4) Add owner membership management UI/API and role-specific behavior.

## 3. BFS — Cross-surface regression and completeness
- [x] C1 Prove tenant isolation for every inventoried surface and asynchronous job under two tenants.
- [x] C2 Verify last-owner, invite replay, CSRF, session expiry, provider outage and migration rollback.
- [x] C3 Reconcile resource inventory to all policies/tests and block deployment for any unscoped surface.

## 4. Verification
- [x] V1 Run .NET/frontend checks and strict OpenSpec validation; run tenant integration and migration suites.
- [x] V2 Produce a reviewed route/table/job isolation matrix with test evidence before enabling hosted traffic.
