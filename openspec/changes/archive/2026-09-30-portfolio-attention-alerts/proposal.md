# Proposal: Portfolio attention alerts

## Why
Daily dashboards require manual checking. Owners need bounded notifications when observable portfolio metrics cross a threshold or become materially stale.

## What Changes
- Configure per-portfolio alert rules for deterministic snapshot metrics and staleness.
- Evaluate rules against persisted data and enqueue deduplicated delivery attempts.
- Provide email and generic webhook delivery with explicit owner configuration and delivery history.

## Package Boundary and Split Assessment
One alert-rule/evaluation/delivery lifecycle; hosted identity and billing are separate packages. Depends on MVP snapshot/metric contracts and is scheduled after the three phase-5 roadmap packages; adoption/commercial-specific alert triggers remain excluded. Evaluation can run self-hosted without cloud accounts.

| Package | Single outcome | Owner/project and language | Boundary/contract | Depends on | Independent oracle |
|---|---|---|---|---|---|
| github-portfolio-momentum-mvp | Deterministic snapshot metrics | Argoscope; C#/.NET 10 | Snapshot and metric read model | — | Fixed portfolio metric fixture |
| package-adoption-metrics | Provider-attributed package observations | Argoscope; C#/.NET 10 | Package association and observation contract | MVP | Unit/window fixture assertions |
| commercial-signal-review | Human-reviewed commercial suggestions | Argoscope; C#/.NET 10 + TypeScript | Versioned suggestion/review contract | MVP | Review transition tests |
| portfolio-decision-journal | Auditable owner decisions | Argoscope; C#/.NET 10 + TypeScript | Decision entry/revision contract | MVP | Audit/readback tests |
| portfolio-attention-alerts | Threshold notifications | Argoscope; C#/.NET 10 | Rule/evaluation/delivery contract | MVP and phase-5 packages | Trigger/deduplication tests |

| Package | Outcome | Owner/runtime | Contract | Depends | Oracle |
|---|---|---|---|---|---|
| github-portfolio-momentum-mvp | Deterministic portfolio metrics | Argoscope/.NET 10 | Snapshot/metric read model | — | Fixed metric fixture |
| portfolio-attention-alerts | Notify owner on configured condition | Argoscope/.NET 10 | AlertRule and DeliveryAttempt | MVP | trigger/idempotency tests |

## Sibling and Shared Architecture Reconnaissance

| Candidate | Evidence | Reuse | Gap | Owner/release | Decision |
|---|---|---|---|---|---|
| dotnet-platform-libs | `../dotnet-platform-libs` Hangfire/web packages | Scheduler and generic infrastructure | No portfolio threshold or alert semantics | Shared library | adapt through a generic adapter |
| Forge | `../forge` notification/provider workflows | Generic operational patterns | Different owner and event lifecycle | Separate product | keep local |

## BFS Impact Map
- Adds alert rule persistence/API/UI, deterministic evaluator job and delivery adapters.
- Secret webhook URLs and SMTP credentials use deployment secret storage; masked values only. Alert payloads contain aggregate metric and repository link, no issue text.
- At-most-one logical alert per rule/metric/window; delivery retries are recorded and bounded. Disabling a rule prevents new evaluations; queued deliveries remain inspectable/cancelable.
- Unchanged: ranking formulas, repository mutation, hosted account, billing and commercial classification.

## Capabilities
- `portfolio-alerts`: evaluate metric thresholds and report delivery state.

## Non-goals
SMS, chat integrations, arbitrary user code/webhooks, alert marketplace, hosted notification guarantees or alerts from unreviewed AI signals.
