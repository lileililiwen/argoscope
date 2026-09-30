# Proposal: Portfolio decision journal

## Why
Portfolio scores show signals but not why an owner chose to continue, pause or archive a project. A dated evidence-linked journal makes later review possible.

## What Changes
- Record owner-authored decisions for a portfolio/repository, rationale, decision date, evidence references and optional review date.
- Preserve amendments as append-only revisions and expose a chronological read view.
- Keep journal authorship distinct from system-generated scores or AI suggestions.

## Package Boundary and Split Assessment
One owner-authored decision lifecycle with independent persistence/API/UI oracle. It depends on MVP portfolio and evidence IDs; package metrics and classifier may be referenced when available but are not required. Commercial review, alerts and hosted capabilities remain separate.

| Package | Outcome | Owner/runtime | Contract | Depends | Oracle |
|---|---|---|---|---|---|
| github-portfolio-momentum-mvp | Portfolio analytics | Argoscope/.NET 10 | Portfolio/repository identity | — | MVP fixture |
| portfolio-decision-journal | Auditable owner decisions | Argoscope/.NET 10, React TS | Decision entry/revision | MVP | revision and evidence readback |

## Sibling and Shared Architecture Reconnaissance

| Candidate | Evidence | Reuse | Gap | Owner/release | Decision |
|---|---|---|---|---|---|
| dotnet-platform-libs | `../dotnet-platform-libs` | Persistence primitives | No portfolio decision semantics | Separate library | keep local |
| Kairion | `../kairion` Evidence Board | Evidence linking pattern | Different evidence and owner decision lifecycle | Product-local | keep local |

## BFS Impact Map
- Adds journal tables, CRUD/read APIs, portfolio detail timeline and evidence links.
- Only the authenticated/self-hosted owner can author; each edit appends revision, with actor/time and previous values.
- Missing/deleted evidence remains as a broken reference marker, not silently removed. Journal text is private local data and excluded from AI prompts and ranking.
- Unchanged: generated scores, lifecycle automation, commercial review, alerts, hosted auth and billing.

## Capabilities
- `decision-journal`: preserve and review owner-authored portfolio decisions.

## Non-goals
AI-authored decisions, automatic decisions, team collaboration, reminders, billing or exported CRM records.
