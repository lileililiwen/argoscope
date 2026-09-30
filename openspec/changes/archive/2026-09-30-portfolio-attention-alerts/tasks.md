# Tasks: Portfolio attention alerts

## 1. BFS — Baseline and impact coverage
- [ ] B1 Map existing metric keys/freshness and R1–R4 paths through API, jobs, persistence and Web.
- [ ] B2 Add fixtures for threshold equality, missing/low coverage, duplicate windows, cooldown and delivery responses.
- [ ] B3 Threat-model webhook resolution, secrets, payload minimization and retry behavior.
- [ ] B4 Confirm metric allowlist, idempotency key and delivery transitions for implementer handoff.

## 2. DFS — Requirement-by-requirement implementation
- [ ] D1 (R1) Add validated alert rules and write-only secret/channel configuration.
- [ ] D2 (R2) Add deterministic evaluator with coverage gate, staleness trigger and unique evaluation key.
- [ ] D3 (R3) Add email/webhook delivery with safe endpoint validation, signing and bounded retries.
- [ ] D4 (R4) Add evaluation/delivery history API and rule-management UI.

## 3. BFS — Cross-surface regression and completeness
- [ ] C1 Verify concurrent/retried evaluator execution yields one logical event and preserves attempts.
- [ ] C2 Verify missing metrics never alert, unsafe webhook destinations fail closed and secrets are redacted.
- [ ] C3 Reconcile rules, schema, evaluator, adapters, API/UI and operational docs.

## 4. Verification
- [ ] V1 Run .NET/frontend format/build/tests and strict OpenSpec validation.
- [ ] V2 Run fake transport integration and SSRF/security cases; no real endpoint required.
