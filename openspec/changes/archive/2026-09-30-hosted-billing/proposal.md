# Proposal: Hosted billing lifecycle

## Why
Paid hosted plans require a reliable subscription state synchronized with an external payment provider, without allowing billing callbacks to grant arbitrary access.

## What Changes
- Define plans, entitlements, customer/subscription mapping and billing state transitions.
- Process signed provider webhooks idempotently and reconcile subscription state.
- Enforce entitlement checks at hosted feature boundaries with user-visible billing state.

## Package Boundary and Split Assessment
Billing owns subscription and entitlement state only; tenant identity is a prerequisite, while deployment, backups and incident operations are a separate package. Dependency: `hosted-multiuser-identity` → `hosted-billing` → `hosted-operations`.

| Package | Outcome | Runtime | Contract | Depends | Oracle |
|---|---|---|---|---|---|
| hosted-multiuser-identity | Tenant/auth isolation | Argoscope/.NET 10 | Tenant/account IDs | MVP | tenant tests |
| hosted-billing | Provider-backed entitlements | Argoscope/.NET 10 | Subscription state/events | Hosted identity | signed webhook idempotency |
| hosted-operations | Operated service | Argoscope/deployment | SLO/backup/restore | Identity + billing | operational rehearsal |

## Sibling and Shared Architecture Reconnaissance

| Candidate | Evidence | Reuse | Gap | Owner/release | Decision |
|---|---|---|---|---|---|
| dotnet-platform-libs | `../dotnet-platform-libs` | Generic web/persistence | No billing provider contract or entitlement policy | Shared library | adapt through a generic adapter |
| Forge | `../forge` billing/tenant-related archived plans | Prior design evidence | Not runtime service and not dependency | Separate product | keep local |

## BFS Impact Map
- Adds billing provider adapter/webhook endpoint, subscription/event tables, entitlement policy, account page and audit.
- Provider is an external payment service selected before implementation; signature verification uses provider SDK/library, idempotency key, timestamp tolerance and event replay protection.
- Raw card data is never handled or stored by Argoscope. Plan changes do not delete user data; grace/suspension behavior is explicit.
- Unchanged: identity protocol, portfolio analytics, alert delivery and infrastructure SLO.

## Capabilities
- `hosted-subscriptions`: synchronize subscriptions and enforce versioned entitlements.

## Non-goals
Card processing, tax advice, invoicing engine, multiple payment providers, usage-based metering or automatic data deletion.
