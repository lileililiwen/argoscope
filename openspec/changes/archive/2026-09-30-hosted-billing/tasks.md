# Tasks: Hosted billing lifecycle

## 1. BFS — Baseline and impact coverage
- [x] B1 Map tenant/account IDs, entitlement-gated surfaces, webhook route and R1–R4 state transitions.
- [x] B2 Build fake signed event fixtures including duplicate, replay, out-of-order, unknown and transient DB failures.
- [x] B3 Resolve payment provider and supported regions before implementation; document contract version and secret custody.
- [x] B4 Confirm no card data storage, idempotent persistence and entitlement cache invalidation.

## 2. DFS — Requirement-by-requirement implementation
- [x] D1 (R1) Add provider customer/subscription/event models and hosted-checkout integration.
- [x] D2 (R2) Verify and process signed webhooks transactionally with replay/idempotency/out-of-order handling.
- [x] D3 (R3) Derive tenant-scoped entitlements and enforce them at feature/API boundaries.
- [x] D4 (R4) Add account billing status, cancellation and reconciliation view/job.

## 3. BFS — Cross-surface regression and completeness
- [x] C1 Verify tenant isolation, event replay, retries and cache invalidation across all entitlement checks.
- [x] C2 Verify canceled/past-due/suspended accounts retain data and follow defined read-only/grace behavior.
- [x] C3 Reconcile provider event inventory, plan mapping, audit, UI and error states.

## 4. Verification
- [x] V1 Run format/build/tests, frontend checks and strict OpenSpec validation.
- [x] V2 Run provider sandbox/test-event suite after provider decision; capture webhook and entitlement evidence.
- [x] V3 Verify payment-provider secrets are absent from repository, logs and API responses.
