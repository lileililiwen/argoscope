# Tasks: Portfolio decision journal

## 1. BFS — Baseline and impact coverage
- [ ] B1 Map portfolio ownership, evidence reference types, journal API/UI and R1–R3 storage boundaries.
- [ ] B2 Add fixtures for create/edit/delete/restore, missing evidence, invalid cross-portfolio link and concurrent update.
- [ ] B3 Confirm local-only privacy and no calls into AI, scores or remote repository writes.
- [ ] B4 Confirm revision and idempotency contract for implementation handoff.

## 2. DFS — Requirement-by-requirement implementation
- [ ] D1 (R1) Add entry/revision persistence and migrations with append-only audit invariants.
- [ ] D2 (R2) Add owner-scoped create/update/soft-delete/restore/list APIs with concurrency checks.
- [ ] D3 (R3) Add validated optional evidence links with unavailable-reference representation.
- [ ] D4 (R1–R3) Add chronological portfolio/repository journal UI and authored-by/revision detail.

## 3. BFS — Cross-surface regression and completeness
- [ ] C1 Verify duplicate requests, ownership boundaries, concurrent revisions and soft-delete recovery.
- [ ] C2 Verify journal text never enters provider prompts, scoring or lifecycle automation.
- [ ] C3 Reconcile R1–R3 across database, API, UI, migration and docs.

## 4. Verification
- [ ] V1 Run .NET format/build/tests, frontend install/build/tests and strict OpenSpec validation.
- [ ] V2 Assert database audit/readback and evidence reference behavior using synthetic portfolio fixtures.
