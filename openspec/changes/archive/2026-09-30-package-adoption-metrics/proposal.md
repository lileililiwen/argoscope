# Proposal: Package adoption metrics

## Why
GitHub stars and forks do not establish whether projects are installed or downloaded. Owners need separately sourced package activity alongside repository metrics.

## What Changes
- Add owner-managed associations between repositories and package coordinates across Docker Hub, npm, NuGet, PyPI and crates.io.
- Collect timestamped provider-reported download/install counts with unit, interval, source and coverage metadata.
- Expose adoption series without interpreting downloads as unique users or production use.

## Package Boundary and Split Assessment
Owns external package-provider observations only. Commercial signal review, decision journal, alerts and hosted capabilities have separate owners and oracles. Dependency: `github-portfolio-momentum-mvp` → `package-adoption-metrics`; commercial signal review and journal independently depend on MVP; alerts depends on MVP; hosted identity → billing → hosted operations.

| Package | Outcome | Owner/runtime | Contract | Depends | Oracle |
|---|---|---|---|---|---|
| github-portfolio-momentum-mvp | Snapshot-based GitHub portfolio | Argoscope/.NET 10, React TS | Repository identity and scheduler | — | Snapshot fixture |
| package-adoption-metrics | Package observations | Argoscope/.NET 10 | Owner-managed package coordinate and provider observations | MVP | Fixture adapter exact counts |
| commercial-signal-review | Human-reviewed commercial signals | Argoscope/.NET 10, React TS | Evidence-linked review item | MVP | Classification/review transition tests |
| portfolio-decision-journal | Auditable owner decisions | Argoscope/.NET 10, React TS | Decision and immutable revisions | MVP | Audit/readback tests |
| portfolio-attention-alerts | Threshold notifications | Argoscope/.NET 10 | Alert rule and delivery attempt | MVP | Deterministic trigger/delivery tests |
| hosted-multiuser-identity | Tenant-isolated accounts | Argoscope/.NET 10 | Tenant-scoped identity/authorization | MVP | Isolation/security tests |
| hosted-billing | Subscription lifecycle | Argoscope/.NET 10 | Billing account and provider events | Hosted identity | Webhook/idempotency tests |
| hosted-operations | Operated hosted service | Argoscope/.NET 10 + deployment | Service SLO/backup/restore contract | Hosted identity, billing | Restore/operational rehearsal |

## Sibling and Shared Architecture Reconnaissance

| Candidate | Evidence | Reuse | Gap | Owner/release | Decision |
|---|---|---|---|---|---|
| dotnet-platform-libs | `../dotnet-platform-libs` | .NET 10 persistence/jobs/web packages | No registry-specific metrics or portfolio semantics | Shared library | adapt through a generic adapter |
| Kairion | `../kairion` | Provider result conventions only | Demand evidence != package metrics | Separate product | keep local |

## BFS Impact Map
- Adds package associations, provider collection jobs, immutable observations, API series and adoption dashboard panels.
- Each provider reports its own measurement unit and period; never sum unlike units or convert counts to unique users.
- Credentials remain deployment secrets; owner must verify package association. Rate-limit, unavailable, deleted and partial data retain last-good observations and visible freshness.
- No score weights change in this package; ranking integration requires explicit configured adoption factor and coverage disclosure.
- Unchanged: GitHub repository mutations, commercial AI, journal, alerts, hosted accounts, billing.

## Capabilities
- `package-adoption`: collect and display source-attributed package activity.

## Non-goals
User-level telemetry, unique installations, private registry scraping, package publishing, automatic repository matching or lifecycle decisions.
