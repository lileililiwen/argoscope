# hosted-billing Specification

## Purpose
Synchronize hosted subscription state safely and enforce tenant-scoped plan entitlements.

## ADDED Requirements

### Requirement: Billing events are authentic and idempotent
The system MUST verify provider signatures and persist each provider event once before acknowledging successful handling.

#### Scenario: Valid event is delivered twice
- **WHEN** a valid provider event is received more than once
- **THEN** it is durably recorded and its state transition is applied once

#### Scenario: Invalid or replayed event
- **WHEN** a signature or timestamp check fails
- **THEN** no subscription state changes and the request is rejected

### Requirement: Entitlements follow tenant subscription state
The system MUST derive entitlements from the tenant's versioned plan/subscription state and enforce them consistently.

#### Scenario: Plan entitlement is removed
- **WHEN** a verified event changes a tenant plan or status
- **THEN** tenant entitlement caches are invalidated and subsequent requests use the new state

#### Scenario: Provider event references another tenant
- **WHEN** an event's provider IDs do not match the persisted tenant mapping
- **THEN** no other tenant's subscription or entitlements change

### Requirement: Billing never handles raw payment credentials
Argoscope MUST use provider-hosted payment entry and MUST NOT store card numbers or security codes.

#### Scenario: Checkout completes
- **WHEN** the owner completes provider-hosted checkout
- **THEN** Argoscope stores provider references and subscription state only

## Traceability

| Requirement | Proposal | Design | Boundary | Scenarios | Tasks | Verification oracle |
|---|---|---|---|---|---|---|
| R1 Authentic events | provider sync | signed event transaction | Webhook/Infrastructure; D1,D2 | duplicate/invalid | B1,B2,D1,D2,C1,V2 | signature/idempotency fixtures |
| R2 Entitlements | plan access | tenant-scoped state/cache | Application/API; D3,D4 | change/cross-tenant | B1,D3,D4,C1,C2,V2 | entitlement transition tests |
| R3 Payment safety | safe checkout | provider-hosted entry | API/Web; D1,D4 | checkout | B3,D1,D4,C3,V3 | data/log inspection |
