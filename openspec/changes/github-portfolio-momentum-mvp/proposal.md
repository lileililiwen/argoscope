# Proposal: GitHub portfolio momentum MVP

## Why

Star totals hide growth, community participation and stale data. An owner needs comparable time-series evidence to allocate effort across owned repositories and competitors.

## What Changes

- Add portfolio/repository membership, GitHub collection, timestamped snapshots, growth and engagement calculations, competitor comparisons, and transparent priority ranking.
- Deliver a self-hosted ASP.NET Core 10 + React/TypeScript application using PostgreSQL, Hangfire, GitHub GraphQL/REST and Docker.
- Keep PATs outside project data and expose rate-limit, permission, freshness and partial-collection status.

## Package Boundary and Split Assessment

| Package | Single outcome | Owner/project and language | Boundary/contract | Depends on | Independent oracle |
|---|---|---|---|---|---|
| github-portfolio-momentum-mvp | Compare and rank owned/competitor repositories from persisted GitHub snapshots | Argoscope; C#/.NET 10 and TypeScript/React | Portfolio → repository membership → provider observations/snapshots → deterministic metric/rank API | Product foundation | Fixed snapshot fixture yields reproducible velocities, engagement separation, peer comparisons and visible data coverage |
| adoption-commercial-signals-and-journal | Add package/download adoption and reviewed commercial evidence with decision journal | Argoscope; C#/.NET 10 | Independent package provider, issue classification and journal contracts | MVP | Provider contracts and journal audit/readback tests |
| portfolio-alerts-and-hosted-accounts | Deliver alerts and hosted multi-user operation | Argoscope; service/deployment boundary | Notification/account/tenant contracts | MVP | Tenant/security and delivery integration evidence |

The MVP metrics and ranking share one snapshot state model and comparison workflow, so they form one usable vertical slice. Adoption sources, commercial classification, alerts, journal, hosted auth and billing have independent lifecycle/security/verification boundaries and are excluded.

## Sibling and Shared Architecture Reconnaissance

| Candidate | Evidence path/symbol | Reusable code/config/architecture | Compatibility gap | Owner and release boundary | Decision |
|---|---|---|---|---|---|
| dotnet-platform-libs | src/Platform.Jobs.Hangfire | .NET 10 recurring-job contracts and Hangfire/PostgreSQL adapter. | Verify release/API versions and whether collection checkpoint semantics fit. | Shared owner maintains scheduler adapter; Argoscope owns GitHub collection jobs and resumability. | **deferred investigation** |
| dotnet-platform-libs | src/Platform.Persistence.EfCore, src/Platform.Persistence.Postgres | EF Core/PostgreSQL conventions and shared persistence composition. | Verify release versions, schema ownership and migration support. | Shared library owns cross-app infrastructure contracts; Argoscope owns time-series schema. | **deferred investigation** |
| Forge | profiles/aspnet-web | Project generation and profile evidence. | Pinned template targets .NET 8; product target is .NET 10. | Forge independently versions templates. | **keep local** |

No sibling will be edited. Before implementation, inspect the package manifest and public release versions, then adopt compatible packages or use local thin adapters. No shared GitHub domain or metric contract is known, so portfolio and score semantics remain Argoscope-owned.

## BFS Impact Map

| Surface | Impact and invariant |
|---|---|
| Actors/flow | Single owner configures portfolio, owned/competitor membership, and ranking weights; dashboard supports review only. |
| Contracts/data | Portfolio, stable repo node identity, membership role, provider observation, daily snapshot, calculation window, category cohort, score config/output. |
| Integration/config | GitHub GraphQL/REST behind provider; least-privilege PAT from secret store; resumable cursor and rate-limit state. |
| Failure/privacy | Permission denial, private/deleted repo, rate limit, stale/partial data are distinct; prior data remains; no token logging. |
| Verification | Pure metric unit tests, provider pagination/error contract tests, PostgreSQL migration/idempotency integration, API/UI disclosure/e2e fixture. |
| Unchanged | No repository mutations, traffic/clones, package downloads, AI classification, alerts, decision journal, OAuth SaaS, billing or automatic lifecycle changes. |

## Capabilities

- Portfolio/repository/competitor management.
- Timestamped GitHub snapshots with permission/freshness/coverage.
- 7/30-day growth, acceleration and external engagement.
- Peer comparison and configurable priority rank.

## Non-goals

Repository management, issue/PR creation, public/private access bypass, historic backfill without observations, star-only “success” score, automatic lifecycle changes, billing, hosted OAuth, package adoption analytics, alerts and AI commercial decisions.
