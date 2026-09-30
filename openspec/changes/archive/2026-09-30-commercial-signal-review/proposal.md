# Proposal: Commercial signal review

## Why
Requests for hosting, enterprise features or paid support can indicate commercialization interest, but automated interpretation must not become an unreviewed business decision.

## What Changes
- Extract eligible GitHub issue/PR text into bounded, privacy-aware classification jobs.
- Suggest a commercial-signal category and confidence with source references.
- Let the portfolio owner accept, reject or correct each suggestion with an audit record.

## Package Boundary and Split Assessment
This package owns suggestion and human review state only. Adoption metrics, journal, alerts and hosted service have independent sources/lifecycles. Depends on `github-portfolio-momentum-mvp`; no dependency on package downloads or journal.

| Package | Outcome | Owner/runtime | Contract | Depends | Oracle |
|---|---|---|---|---|---|
| github-portfolio-momentum-mvp | Portfolio snapshots and repo identity | Argoscope/.NET 10 | Repo identity and GitHub read boundary | — | MVP fixture |
| commercial-signal-review | Reviewable commercial-interest evidence | Argoscope/.NET 10 + React TS | `SignalSuggestion` and `SignalReview` | MVP | state-transition/API tests |

## Sibling and Shared Architecture Reconnaissance

| Candidate | Evidence | Reuse | Gap | Owner/release | Decision |
|---|---|---|---|---|---|
| dotnet-platform-libs | `../dotnet-platform-libs` AI contracts | Generic model/provider plumbing | No portfolio issue semantics or review audit | Shared AI package; product review local | adapt through a generic adapter |
| Kairion | `../kairion` AI evidence patterns | Structured output conventions | Demand intent differs from commercial request | Separate product | keep local |

## BFS Impact Map
- Affected: GitHub issue/PR read collector, bounded text projection, classifier adapter, signal/review persistence, API and review queue UI.
- Existing MVP least privilege remains; private repositories require owner authorization. Store source URL/number and minimal excerpt only, with redaction and retention policy.
- Provider/model failure yields retryable unclassified state. Suggestions never alter score, repository, labels or lifecycle automatically.
- Unchanged: package metrics, journal, alerts, billing, hosted identity, GitHub write scopes.

## Capabilities
- `commercial-signal-review`: owner-reviewed classification of commercial requests.

## Non-goals
Automatic sales outreach, issue mutation, lead export, pricing decisions, or treating AI output as validated demand.
