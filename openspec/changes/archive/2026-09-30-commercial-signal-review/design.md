# Design: Commercial signal review

## Implementation boundary
Argoscope .NET 10 Application/Infrastructure/API plus React/TypeScript Web. Extend read-only GitHub collection with eligible issue/PR metadata; add local classifier contract, queued job, `SignalSuggestion` and `SignalReview` tables, review API and queue UI. No GitHub mutation endpoint or shared library edit.

## Language and runtime
C# / ASP.NET Core 10, PostgreSQL, Hangfire; React/TypeScript. Commands: .NET format/build/test, npm ci/build/test, strict OpenSpec validation.

## Ownership and shared code
Argoscope owns signal vocabulary, evidence minimization and human decisions. Evaluate dotnet-platform-libs AI package compatibility, but keep application DTOs and review lifecycle local; no model SDK types outside Infrastructure.

## Behavioral model
Eligible sources are issue/PR title and body, with comments excluded in v1. A queued record stores repository ID, issue/PR number, URL, source updated-at, content hash and redacted excerpt capped at 4,000 characters. Categories: `HostedRequest`, `PaidSupport`, `EnterpriseCapability`, `ProcurementQuestion`, `NotCommercial`, `Unclear`. Classifier returns category, confidence [0,1], schema version and short rationale referencing source excerpt. Duplicate source version is idempotent; changed content creates a new suggestion version. Review transitions `Pending → Accepted|Rejected|Corrected`; reviewer/time and prior values are immutable audit events. Reclassification does not overwrite a human decision; it creates a new pending version.

## Contract and compatibility
`GET /api/repositories/{repoId}/commercial-signals?state=` returns source ref, category, confidence, status, suggestion version and review history. Review PATCH requires `expectedVersion`, decision and optional corrected category; stale version gives 409. AI key/provider uses existing BYOK config; if unavailable, records retryable `Unclassified`. Store no author email, tokens or full thread. Retention default 365 days for minimized text, configurable downwards; references and audit metadata remain until owner deletion. API read-only toward GitHub.

## Failure and boundary policy
Malformed classifier output is rejected and marked retryable; low confidence (<0.65) defaults `Unclear`; source deletion retains review audit and marks source unavailable; duplicate job is idempotent; concurrent review conflicts return 409; no comments/text means no suggestion. No AI results affect portfolio rank.

## Verification oracle
Fake classifier fixtures cover every category, malformed JSON, low confidence, prompt-injection text and provider failure. Tests assert redaction/truncation, content-version idempotency, immutable audit transitions, stale-version conflict, and no GitHub write calls. API/UI tests verify source links, pending/reviewed state and noncommercial label. Build/test/strict validation required.

## Decision ledger
- Only issue/PR title and body in v1; comments excluded.
- Owner review is mandatory before a signal is considered confirmed.
- Classifier is advisory; no factor changes, outreach or repository mutations.
- Use existing provider abstraction after compatibility review; no separate AI service.
