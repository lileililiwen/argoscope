# Tasks: Commercial signal review

## 1. BFS — Baseline and impact coverage
- [ ] B1 Map permitted GitHub read scopes, text lifecycle, classifier, review UI and R1–R4 callers.
- [ ] B2 Add synthetic issue/PR fixtures for categories, prompt injection, redaction, edits, deletion and provider failure.
- [ ] B3 Confirm provider/model compatibility and secret boundaries without adding GitHub write permissions.
- [ ] B4 Confirm immutable review transition contract and implementation-handoff gate.

## 2. DFS — Requirement-by-requirement implementation
- [ ] D1 (R1) Collect eligible issue/PR title/body with bounded projection and source version identity.
- [ ] D2 (R2) Implement typed classifier, category validation, confidence threshold and retryable failures.
- [ ] D3 (R3) Persist versioned suggestions and append-only review audit with optimistic concurrency.
- [ ] D4 (R4) Add scoped API and review queue UI; keep score and GitHub resources unchanged.

## 3. BFS — Cross-surface regression and completeness
- [ ] C1 Verify edited/deleted sources, duplicate jobs, stale reviews and concurrent reviewer conflict.
- [ ] C2 Prove classifier never mutates GitHub or changes portfolio rank; inspect stored excerpt for minimization.
- [ ] C3 Reconcile R1–R4 and all schema/UI/error paths; remove placeholders.

## 4. Verification
- [ ] V1 Run format/build/tests for .NET and frontend and strict OpenSpec validation.
- [ ] V2 Assert fake-provider classification, rejection, correction and audit readback without live model credentials.
