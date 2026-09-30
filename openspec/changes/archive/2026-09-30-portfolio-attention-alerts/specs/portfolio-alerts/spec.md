# portfolio-alerts Specification

## Purpose
Notify the portfolio owner about configured, deterministic metric conditions with inspectable delivery outcomes.

## ADDED Requirements

### Requirement: Alert rules use supported portfolio metrics
The system MUST validate alert rules against an allowlist and require adequate metric coverage before evaluation.

#### Scenario: Metric crosses configured threshold
- **WHEN** a sufficiently covered metric satisfies an enabled rule
- **THEN** one logical alert evaluation is recorded for that rule and metric window

#### Scenario: Metric is missing or under-covered
- **WHEN** the metric is unavailable or below the rule's minimum coverage
- **THEN** evaluation is skipped with a reason and no delivery is created

### Requirement: Delivery is bounded, deduplicated and observable
The system MUST record delivery attempts, prevent duplicate logical notifications, and expose terminal or retryable outcomes.

#### Scenario: Delivery transiently fails
- **WHEN** a configured destination returns a retryable error
- **THEN** Argoscope records the attempt and schedules a bounded retry

#### Scenario: Same evaluation runs twice
- **WHEN** a job is retried for the same rule and metric window
- **THEN** it does not create a duplicate logical alert

### Requirement: Destinations and secrets are protected
The system MUST reject unsafe webhook targets and MUST NOT expose channel secrets in API responses or logs.

#### Scenario: Configure unsafe webhook
- **WHEN** a webhook resolves to a loopback/private/link-local address or uses non-HTTPS
- **THEN** configuration or delivery is rejected and no request is sent

## Traceability

| Requirement | Proposal | Design | Boundary | Scenarios | Tasks | Verification oracle |
|---|---|---|---|---|---|---|
| R1 Rule/coverage | threshold alerts | allowlist and coverage gate | Application; D1,D2 | crosses/missing | B1,B2,D1,D2,C2,V2 | fixed threshold fixtures |
| R2 Delivery | notification lifecycle | state machine/idempotency | Jobs/Infrastructure/API; D3,D4 | retry/duplicate | B2,D3,D4,C1,V2 | fake transport attempts |
| R3 Security | safe channels | SSRF/secret rules | Infrastructure; D1,D3 | unsafe target | B3,D1,D3,C2,V2 | destination security tests |
