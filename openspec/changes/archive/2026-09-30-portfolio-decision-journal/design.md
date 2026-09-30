# Design: Portfolio decision journal

## Implementation boundary
Argoscope .NET 10 Core/Application/Infrastructure/API and React/TypeScript Web. Add `DecisionEntry`, `DecisionRevision` and evidence-reference mappings, owner-scoped API and repository/portfolio timeline. Reuse portfolio/evidence identities; do not alter score lifecycle or shared libraries.

## Language and runtime
C# / ASP.NET Core 10, EF Core/PostgreSQL; React/TypeScript. Verify .NET formatting/build/tests, frontend install/build/tests and strict OpenSpec validation.

## Ownership and shared code
Argoscope owns decision vocabulary and journal persistence. No shared project contains compatible portfolio journal state. No cross-project dependency.

## Behavioral model
Entry fields: ID, portfolio ID, optional repository ID, decision type (`Continue|Invest|Pause|Archive|Revisit`), decision date UTC, rationale (1–10,000 chars), optional review date UTC, created time, current revision. Each create/edit appends immutable revision with actor ID, timestamp and before/after values. Evidence references are typed stable IDs/URLs to Argoscope snapshot, commercial signal or package observation; references are optional and never copied into rationale automatically. Delete is soft-delete with an audit revision; owner can restore. Query ordered by decision date descending then created UTC descending.

## Contract and compatibility
REST create/update/delete/restore and list endpoints require portfolio ownership. Update requires `expectedRevision`; mismatch returns 409. Validation errors 400, missing portfolio/entry 404. API serializes UTC ISO-8601 and returns revision history paginated (100 maximum/page). New tables/endpoints are additive. Journal data is never sent to external AI/provider calls. No schema stores credentials.

## Failure and boundary policy
Missing evidence ref is retained as `Unavailable` on read. Cross-portfolio reference is rejected. Duplicate client retry with idempotency key returns original entry. Invalid review date/rationale rejects without revision. Concurrent update conflict leaves history unchanged. Soft-deleted entries hidden by default but retrievable/restorable by owner.

## Verification oracle
Domain tests assert legal decisions and revision ordering; API tests cover ownership, validation, idempotency and optimistic concurrency; PostgreSQL tests prove append-only audit; UI tests assert authored-by labeling, evidence broken-link state and chronological ordering. Build/test/strict validation commands above.

## Decision ledger
- Journal decisions are user-authored immutable revisions, including corrections/deletion events.
- Entries are advisory records and never drive automatic project state or scores.
- Reminders and team authorship are deferred separate capabilities.
