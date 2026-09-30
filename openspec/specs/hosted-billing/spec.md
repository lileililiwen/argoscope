# hosted-billing Specification

## Purpose
TBD - created by archiving change hosted-billing. Update Purpose after archive.
## Requirements
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

