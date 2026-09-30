# Proposal: Hosted operations readiness

## Why
Hosted identity and billing do not establish that Argoscope can be operated reliably. Release needs explicit deployment, recovery, security and incident evidence.

## What Changes
- Define production deployment and configuration boundaries for the hosted Argoscope service.
- Establish observable service health, backup/restore, migration rollout, incident response and data-retention procedures.
- Gate public hosted launch on rehearsed recovery and operational evidence.

## Package Boundary and Split Assessment
This package owns hosted deployment/operational readiness; it does not implement identity or billing. Depends on `hosted-multiuser-identity` and `hosted-billing`. Alert delivery is not the service-health notification channel and remains separate.

| Package | Outcome | Owner/runtime | Boundary | Depends | Oracle |
|---|---|---|---|---|---|
| hosted-multiuser-identity | Authenticated tenant service | Argoscope/.NET 10 | Tenant-scoped application | MVP | tenant isolation |
| hosted-billing | Entitlement state | Argoscope/.NET 10 | Payment provider events | Hosted identity | webhook suite |
| hosted-operations | Repeatable operated service | Argoscope/deployment | Production runtime and data operations | Identity + billing | restore/incident rehearsal |

## Sibling and Shared Architecture Reconnaissance

| Candidate | Evidence | Reuse | Gap | Owner/release | Decision |
|---|---|---|---|---|---|
| dotnet-platform-libs | `../dotnet-platform-libs` web/jobs/persistence | Runtime packages | No hosted Argoscope SLO, backup or incident policy | Shared libs vs service operations | adapt through a generic adapter |
| Forge | `../forge` project deployment workflows | Generic deployment contract patterns | Not production operator for Argoscope | Separate platform | keep local |

## BFS Impact Map
- Affects production deployment manifests, configuration/secrets, migrations, telemetry, database backup/restore, retention, runbooks and release gate.
- Define SLOs, RPO/RTO and hosting region as explicit operator decisions before production; do not claim readiness without measured rehearsal.
- Tenant deletion/export is covered by a documented privacy workflow; backups have bounded retention and access audit.
- Unchanged: application feature behavior, payment provider logic, source metrics and local self-hosted deployment.

## Capabilities
- `hosted-service-operations`: deploy, observe, recover and support the hosted service with measured evidence.

## Non-goals
Choosing a cloud vendor, promising unmeasured availability, implementing new product features, or replacing self-hosted Docker support.
