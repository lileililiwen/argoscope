# Sibling and shared architecture reconnaissance

Records the local `dotnet-platform-libs` evaluation performed before adding web,
persistence or job infrastructure to Argoscope. Source evidence is the sibling
repository at `../dotnet-platform-libs` and the public NuGet feed.

## Decision summary

| Concern | Sibling surface | Sibling release status | Decision | Local adapter |
| --- | --- | --- | --- | --- |
| Core contracts (`IClock`, `Result<T>`, `Error`, `CallerContext`, `IAuditable`) | `src/Platform.Core` | Local source only; not published to nuget.org. The published `Platform.Core` package on nuget.org is owned by `fbasa`, targets .NET Framework 4.5.2 and is unrelated. | local | `Argoscope.Domain/Common` (small surface) |
| Domain primitives (`IEntity<TId>`, `IAggregateRoot<TId>`, `IDomainEvent`, `Entity<TId>`, `AggregateRoot<TId>`, `DomainException`) | `src/Platform.Domain` | Local source only; not published to nuget.org. The published `Platform.Domain` package on nuget.org is owned by `nazinstas`, targets .NET Standard 2.1 and is unrelated. | local | `Argoscope.Domain/Common` |
| ASP.NET Core composition (`IPlatformWebModule`, `AddPlatformWebModule`, correlation middleware) | `src/Platform.AspNetCore`, `src/Platform.Web.Composition` | Local source only; not published to nuget.org. | local | Standard ASP.NET Core 10 `Program.cs` + minimal-API mapping, no shared module registry. |
| EF Core / PostgreSQL | `src/Platform.Persistence.EfCore`, `src/Platform.Persistence.Postgres`, `src/Platform.Persistence.EfCore.Migrator` | Local source only; not published to nuget.org. | local | `Argoscope.Infrastructure/Persistence` using `Microsoft.EntityFrameworkCore` 10.0.0 and `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.0. |
| Job scheduling | `src/Platform.Jobs`, `src/Platform.Jobs.Hangfire` | Local source only; not published to nuget.org. The Hangfire packages themselves are public OSS and version-pinned by the sibling. | local | `Argoscope.Infrastructure/Scheduling/DailyCollectionHostedService` using `BackgroundService` and `TimeProvider`; matches the spec's intent for a single daily collection run, avoids the platform's published-only Hangfire dependency, and keeps the daily job resume/cursor logic in Argoscope-owned code. |
| Project generation | `Forge` (sibling) | Pinned template targets .NET 8. | keep local | Argoscope scaffolded directly; no template invocation. |

## Why local for core contracts

The published `Platform.Core` 2.0.0 / `Platform.Domain` 1.0.46 packages on
nuget.org are owned by different maintainers, target older frameworks, and pull
heavy unrelated dependencies (`AutoMapper`, `OWIN`, `Grpc`, `Microsoft.AspNet.Identity.EntityFramework`).
Adopting them would couple Argoscope to a third-party API that is not the local
sibling and would not satisfy the spec's "no source is copied" rule. Adopting the
local sibling as a project reference would require editing the sibling, which the
AGENTS.md explicitly forbids. The smallest Argoscope-owned surface for the few
contracts we use (`IClock`, `Result<T>`, `Error`, entity base) is therefore
preferred.

## What we did not vendor

- No source is copied from `dotnet-platform-libs`. The sibling is read for
  surface evaluation only.
- No `Platform.*` NuGet package is referenced.
- No edit is performed against `../dotnet-platform-libs`.

## Future adoption path

When the local `Platform.Core`, `Platform.Domain`, `Platform.Persistence.EfCore`
and `Platform.Jobs` packages are published to nuget.org, the local adapters can
be replaced with `PackageReference` and the change tracked in
`openspec/changes/<id>/proposal.md`. The replacement must keep the same public
contract names so that consuming code in Argoscope does not change.
