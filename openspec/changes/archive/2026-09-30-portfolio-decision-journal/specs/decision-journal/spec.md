# decision-journal Specification

## Purpose
Keep a private, auditable record of owner-authored portfolio decisions and their evidence.

## ADDED Requirements

### Requirement: Owner decisions retain revision history
The system MUST store a decision, rationale, UTC date and optional review date, and MUST append a revision for each mutation.

#### Scenario: Create and amend decision
- **WHEN** the owner creates a decision and later edits it with the current revision
- **THEN** the current entry is updated and both original and amended values remain in ordered history

#### Scenario: Concurrent edit uses stale revision
- **WHEN** an update supplies an obsolete expected revision
- **THEN** the API returns conflict and leaves the entry and history unchanged

### Requirement: Evidence links remain scoped and inspectable
The system MUST validate evidence references against the same portfolio and disclose references that later become unavailable.

#### Scenario: Link same-portfolio evidence
- **WHEN** an owner links valid portfolio evidence
- **THEN** the journal entry exposes the typed reference and its source destination

#### Scenario: Evidence is later unavailable
- **WHEN** a referenced item has been removed or cannot be resolved
- **THEN** the journal preserves the reference and marks it unavailable

### Requirement: Journal does not automate portfolio decisions
Journal entries MUST remain owner-authored and MUST NOT alter scores, lifecycle state, or external repositories.

#### Scenario: Record a pause decision
- **WHEN** the owner records a pause decision
- **THEN** the lifecycle and score remain unchanged until separately changed by the owner

## Traceability

| Requirement | Proposal | Design | Boundary | Scenarios | Tasks | Verification oracle |
|---|---|---|---|---|---|---|
| R1 Revision history | owner journal | DecisionRevision | Core/Persistence/API; D1,D2 | create/amend/conflict | B1,B2,D1,D2,C1,V2 | append-only revision DB test |
| R2 Evidence links | evidence rationale | typed project-scoped refs | Application/API/Web; D3,D4 | valid/unavailable | B2,D3,D4,C1,V2 | scope and unresolved-ref tests |
| R3 Manual-only | no automation | separate journal state | scoring/lifecycle; D4 | pause unchanged | B3,D4,C2,V1 | score/lifecycle unchanged assertion |
