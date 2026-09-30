# Tasks: Hosted operations readiness

## 1. BFS — Baseline and impact coverage
- [x] B1 Inventory deploy/runtime/DB/job/secret/telemetry surfaces and map R1–R4.
- [x] B2 Define provider-independent readiness fixtures for migration, backup, restore, deletion and incident exercise.
- [x] B3 Select host/region and approve SLO/RPO/RTO/retention targets before provider-specific work.
- [x] B4 Confirm dependency gates: hosted identity and billing complete, all prior migrations applied.

## 2. DFS — Requirement-by-requirement implementation
- [x] D1 (R1) Add immutable artifact, staged migration and readiness-gated deployment flow.
- [x] D2 (R3) Add secret injection, health/telemetry, access audit and operational alert integration.
- [x] D3 (R2) Implement encrypted backup, isolated restore and tenant deletion/backup expiry procedures.
- [x] D4 (R3) Add runbooks, incident/status communication and evidence capture for measured recovery objectives.

## 3. BFS — Cross-surface regression and completeness
- [x] C1 Rehearse upgrade/rollback, failed readiness, backup restore and deletion against production-like data volume.
- [x] C2 Verify logs, metrics, health and artifacts contain no credentials or cross-tenant data.
- [x] C3 Reconcile SLO and operational claims with measured evidence; block launch where evidence is absent.

## 4. Verification
- [x] V1 Run repository CI, container security/build, migration and strict OpenSpec validation.
- [x] V2 Record operator-signed restore/incident evidence with measured RPO/RTO and artifact revision.
- [x] V3 Confirm hosted identity and billing acceptance evidence before launch approval.
