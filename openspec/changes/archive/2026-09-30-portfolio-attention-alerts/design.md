# Design: Portfolio attention alerts

## Implementation boundary
Argoscope .NET 10 Application/Infrastructure/API, PostgreSQL, Hangfire, React/TypeScript. Add `AlertRule`, `AlertEvaluation`, `DeliveryAttempt`, evaluator job and email/webhook adapters; dashboard alert settings/history. Reuse existing metric query contract; no shared project changes.

## Language and runtime
C# / ASP.NET Core 10, EF Core/PostgreSQL, Hangfire; React/TypeScript. Verify format/build/test and frontend install/build/test plus strict OpenSpec validation.

## Ownership and shared code
Argoscope owns rule and dedupe semantics. dotnet-platform-libs may supply a compatible job primitive after review; transport/provider-specific alert contracts stay local.

## Behavioral model
Rule has metric key from allowlist (`stars_7d`, `stars_30d`, `external_engagement_30d`, `momentum_score`, `snapshot_staleness_hours`), operator, threshold, minimum coverage, cooldown, enabled, delivery-channel reference. Evaluator runs after successful snapshot batch and once daily for staleness. One evaluation key `(ruleId, metricWindowEndUtc)` prevents duplicates. Unknown/missing/low-coverage metrics yield `SkippedInsufficientData`; they do not fire. Delivery state: Queued → Sending → Delivered|RetryableFailure|PermanentFailure|Canceled. Max 5 attempts with bounded exponential delay; webhook URL must be HTTPS and private-network/loopback targets are rejected at resolution to mitigate SSRF.

## Contract and compatibility
Rule CRUD validates allowlisted key/operator/range; secrets are write-only and masked. Delivery payload has event ID, rule name, repository identity, metric key/value/window, as-of time and dashboard URL. Webhook HMAC signature uses secret reference. `GET /api/alerts` lists evaluation/delivery states. New tables/routes are additive; no changes to metric score. Error responses follow existing API envelope; stale optimistic update returns 409.

## Failure and boundary policy
Missing data skips with reason; duplicate evaluator no-ops; provider outage does not fabricate metrics; transient delivery retries; 4xx webhook except 429 is permanent; DNS/IP is revalidated on every send; endpoint secret never appears in logs/API. Rule deletion soft-disables and preserves history. Email configuration unavailable is a visible permanent failure.

## Verification oracle
Fixed metric fixtures assert threshold boundaries, coverage gating, staleness, cooldown and dedupe. Fake transport asserts payload/signature/retry states. Security tests reject HTTP, loopback, private, link-local and rebinding-resolved private targets. API/UI tests assert masking and history. Build/test/strict validation required.

## Decision ledger
- Only the allowlisted MVP metric set is supported initially.
- Alert delivery is best effort with visible attempts; no SLA claim.
- Do not include raw issue/PR text or AI-generated signals.
