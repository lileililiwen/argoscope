# Design: Hosted billing lifecycle

## Implementation boundary
Argoscope .NET 10 API/Application/Infrastructure/Web. Add billing provider adapter, webhook endpoint, `BillingCustomer`, `Subscription`, `ProviderEvent`, `EntitlementGrant` persistence and account billing UI. Require hosted-multiuser-identity tenant IDs. No deployment/SLO changes and no shared repo edits.

## Language and runtime
C# / ASP.NET Core 10, PostgreSQL, Hangfire for reconciliation; React/TypeScript. Verify .NET format/build/tests, frontend install/build/tests and strict OpenSpec validation.

## Ownership and shared code
Argoscope owns plan catalog and feature entitlement rules. Payment provider owns payment instrument/transaction data. Use provider-supported SDK and webhook verification package; no card data is stored by Argoscope.

## Behavioral model
One configured payment provider. Subscription state: `Trial|Active|PastDue|Canceled|Suspended`; provider event state: `Received|Applied|Ignored|Failed`. Events unique by provider event ID; handler validates signature and timestamp tolerance ±5 minutes, persists event and state mutation transactionally. Entitlements derive from plan + normalized subscription state, versioned and cached with tenant key. `PastDue` has 7-day grace; `Canceled` remains active through paid-through date; `Suspended` is read-only for 30 days then account deletion requires separate owner policy. Data is never auto-deleted in this package.

## Contract and compatibility
Webhook returns 2xx only after event durably recorded; duplicate ID returns 2xx with no repeated effect; invalid signature 400; temporary DB failure 5xx. Public billing API exposes plan, status, period end, cancellation flag and entitlements, never payment method details. Entitlement denial returns 402-shaped application error with stable code and upgrade URL. Provider customer/subscription IDs are unique and tenant-bound. Provider and plan IDs configured explicitly; no hidden default plan.

## Failure and boundary policy
Out-of-order events are recorded and ignored when provider-created timestamp is older than applied event. Unknown event type is durably ignored. Reconciliation job compares active provider subscriptions daily and reports mismatches; it does not silently overwrite tenant data. Provider outage uses last-known entitlement only through explicit grace window, then read-only. Webhook poison event is quarantined and alerted; retry is idempotent.

## Verification oracle
Provider test SDK fixtures verify valid/invalid signatures, replay, duplicate/out-of-order/unknown events, transaction rollback and entitlement transitions. Tenant tests assert an event cannot update another tenant subscription. API/UI tests assert no payment secrets and stable entitlement error. Build/test/strict validation required.

## Decision ledger
- Select one hosted payment provider before implementation; this is an explicit blocker because region and legal entity affect availability.
- No raw card data; payment-hosted checkout only.
- No automatic deletion on cancellation/suspension.
- Billing cannot be implemented before tenant identity is complete.
