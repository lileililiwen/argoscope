# Argoscope roadmap

Status: all eight OpenSpec changes are implemented, verified and archived. Order describes dependencies, not delivery dates.

| Order | Outcome | Depends on | Acceptance boundary | State |
|---|---|---|---|---|
| 1 | Portfolio and repository catalog | — | Owned and competitor repos have explicit role, owner, lifecycle and stable GitHub identity. | shipped |
| 2 | GitHub collection and daily snapshots | 1 | Rate-limited collection persists timestamped metrics with source, permission, freshness and partial-failure evidence. | shipped |
| 3 | Growth, acceleration and external engagement | 2 | Metrics are reproducible from snapshots and distinguish owner from external activity. | shipped |
| 4 | Competitor benchmarking and priority ranking | 3 | Comparable peer baselines and configurable scores expose inputs, missing data and coverage. | shipped |
| 5 | Adoption, commercial signals and decision journal | 4 | Package usage, commercial asks and decisions have independent evidence and human review. | shipped |
| 6 | Alerts and hosted operations | 5 | Alert delivery, multi-user auth, billing and SaaS have separate security and operations acceptance. | shipped |

Archived changes: `github-portfolio-momentum-mvp`, `package-adoption-metrics`, `portfolio-decision-journal`, `portfolio-attention-alerts`, `commercial-signal-review`, `hosted-multiuser-identity`, `hosted-billing`, `hosted-operations`. Specs live under `openspec/specs/`.

## Deferred scope

Traffic and clone analytics requiring owner permissions and automatic lifecycle decisions remain deferred. Package downloads, commercial-signal review, decision journal, alerts, hosted auth, billing and operations readiness all shipped.

## Shared capability checkpoint

dotnet-platform-libs owns .NET 10 web, persistence and Hangfire contracts. Published packages compatible with .NET 10, EF Core 10 and the chosen hosting model were evaluated during implementation (see `docs/architecture/platform-evaluation.md`); this repository keeps its local thin adapters and direct EF Core + Npgsql and does not modify the shared library.
