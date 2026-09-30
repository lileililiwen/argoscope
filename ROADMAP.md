# Argoscope roadmap

Status: planning only; no runtime implementation exists. Order describes dependencies, not delivery dates.

| Order | Outcome | Depends on | Acceptance boundary |
|---|---|---|---|
| 1 | Portfolio and repository catalog | — | Owned and competitor repos have explicit role, owner, lifecycle and stable GitHub identity. |
| 2 | GitHub collection and daily snapshots | 1 | Rate-limited collection persists timestamped metrics with source, permission, freshness and partial-failure evidence. |
| 3 | Growth, acceleration and external engagement | 2 | Metrics are reproducible from snapshots and distinguish owner from external activity. |
| 4 | Competitor benchmarking and priority ranking | 3 | Comparable peer baselines and configurable scores expose inputs, missing data and coverage. |
| 5 | Adoption, commercial signals and decision journal | 4 | Package usage, commercial asks and decisions have independent evidence and human review. |
| 6 | Alerts and hosted operations | 5 | Alert delivery, multi-user auth, billing and SaaS have separate security and operations acceptance. |

## Deferred scope

Traffic and clone analytics requiring owner permissions, Docker Hub/npm/NuGet/PyPI/crates downloads, AI issue/commercial classification, alerts, decision journal, hosted OAuth, billing and automatic lifecycle decisions are deferred from MVP.

## Shared capability checkpoint

dotnet-platform-libs owns .NET 10 web, persistence and Hangfire contracts. Evaluate published package compatibility during implementation; this repository owns GitHub metric semantics and portfolio scoring and does not modify the shared library.
