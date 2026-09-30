# Proposal: Hosted multi-user identity

## Why
Hosted operation requires account authentication and strict tenant boundaries before multiple owners can safely manage portfolios.

## What Changes
- Add hosted account sign-in, tenant membership and role-based authorization.
- Scope every portfolio and child resource to a tenant and enforce isolation in application and persistence queries.
- Provide auditable membership lifecycle and account recovery flows.

## Package Boundary and Split Assessment
This package owns identity and tenant authorization only. Billing and operated deployment have separate security, provider and operational oracles. Dependency chain: self-hosted MVP → the three phase-5 roadmap packages → hosted identity → hosted billing → hosted operations. Alerts remains its own phase-6 outcome.

| Package | Outcome | Owner/runtime | Contract | Depends | Oracle |
|---|---|---|---|---|---|
| github-portfolio-momentum-mvp | Single-owner portfolio model | Argoscope/.NET 10 | Portfolio ownership | — | MVP tests |
| hosted-multiuser-identity | Tenant-isolated authenticated access | Argoscope/.NET 10 | Tenant principal and authorization | MVP | cross-tenant denial tests |
| hosted-billing | Paid subscription lifecycle | Argoscope/.NET 10 | Account/subscription events | Hosted identity | webhook idempotency tests |
| hosted-operations | Operated hosted service | Argoscope/deployment | Backup, restore, incident contract | Identity, billing | restore rehearsal |

## Sibling and Shared Architecture Reconnaissance

| Candidate | Evidence | Reuse | Gap | Owner/release | Decision |
|---|---|---|---|---|---|
| dotnet-platform-libs | `../dotnet-platform-libs` web/auth primitives (compatibility to inspect at implementation) | Generic ASP.NET integration | Tenant policy/data isolation is product-specific | Shared package vs product release | adapt through a generic adapter |
| OpenAccount | workspace sibling | Identity concepts | Separate application/user lifecycle; no inferred federation | Separate release | keep local |

## BFS Impact Map
- Affects authentication, tenant/member persistence, portfolio ownership migration, every API authorization path, web session and audit logs.
- Existing single-owner data must be assigned to an explicit initial tenant through a reviewed migration; no row may be exposed before assignment.
- Use an established OIDC provider; Argoscope does not store passwords. Roles: `Owner`, `Editor`, `Viewer`; least privilege by default.
- Unchanged: billing/payment processing, hosted infrastructure/SLO and GitHub scopes.

## Capabilities
- `hosted-tenant-identity`: authenticate accounts and isolate tenant-owned portfolio data.

## Non-goals
Password database, public sharing, organization directory sync, cross-tenant benchmarking, payment, or deployment operations.
