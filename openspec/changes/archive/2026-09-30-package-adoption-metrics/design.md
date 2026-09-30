# Design: Package adoption metrics

## Implementation boundary
Argoscope .NET 10 `Argoscope.Core`, `Application`, `Infrastructure`, `Api`, React/TypeScript Web. Add local `IPackageMetricsProvider`, package association endpoints/model, provider adapters, Hangfire collection job, PostgreSQL observations, dashboard chart. Do not alter GitHub provider or shared libraries.

## Language and runtime
C# / ASP.NET Core 10, EF Core/PostgreSQL, Hangfire; React/TypeScript. Verify with `dotnet format --verify-no-changes`, `dotnet build`, `dotnet test`, `npm ci`, `npm run build`, `npm test`, strict OpenSpec validation.

## Ownership and shared code
Argoscope owns association, normalized provider observations and adoption presentation. Evaluate dotnet-platform-libs package compatibility at implementation, without sibling edits; registry SDKs remain Infrastructure-only.

## Behavioral model
Owner explicitly associates `(provider, package-coordinate, repositoryId)`; coordinates are unique per provider/account. Collection records `PackageObservation(packageId, observedAtUtc, windowStartUtc, windowEndUtc, value, unit, provider, status)`. Repeated provider/window reads upsert idempotently. A failed run does not overwrite last-good data. Public providers need no token unless official access requires one; any token is deployment secret only. Provider scheduling is daily, concurrency one per provider/package, bounded retries three.

## Contract and compatibility
`GET /api/repositories/{id}/adoption` returns observations, per-series provider/unit/window, freshness and coverage; incompatible units stay separate. Association CRUD validates provider-specific coordinate syntax and explicit confirmation. Additive schema/API; old dashboards render with adoption unavailable. Missing data never becomes zero. Never describe a download as a person, installation, active usage or revenue.

## Failure and boundary policy
| Case | Behavior |
|---|---|
| No linked package | Empty series, unavailable coverage |
| Invalid coordinate | 400; no association |
| Provider 429/5xx/timeout | Retry boundedly, preserve last-good, mark stale |
| Provider reports zero | Persist explicit zero with interval/unit |
| Duplicate observation | Idempotent upsert |
| Package deleted/renamed | Mark association attention-required; retain history |
| Unit/window differs | Separate series; never aggregate |

## Verification oracle
Fake providers assert coordinate validation, unit/window preservation, paging, retries and safe diagnostics. DB tests assert idempotent observations and last-good retention. API/UI tests assert empty-vs-zero, freshness and no unsupported usage claims. Build/test/strict validation commands above.

## Decision ledger
- MVP ranking remains unchanged until adoption-factor configuration is separately accepted and coverage behavior is defined.
- Package owner association is manual to prevent false matches.
- Deferred: private registries, user telemetry, billing or automated package discovery.
