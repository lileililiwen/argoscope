# hosted-operations Specification

## Purpose
Establish measurable operational readiness and recovery controls for hosted Argoscope.

## ADDED Requirements

### Requirement: Hosted releases are health and migration gated
The release process MUST deploy immutable revision-tagged artifacts, apply compatible migrations, and block traffic promotion when readiness fails.

#### Scenario: Readiness succeeds
- **WHEN** the migration and service readiness checks pass for the candidate release
- **THEN** the release may receive traffic and records its source revision

#### Scenario: Migration or readiness fails
- **WHEN** migration or readiness fails
- **THEN** traffic promotion stops and the prior compatible application remains available

### Requirement: Backups are recoverable within declared objectives
The operator MUST retain encrypted backups and demonstrate isolated restore against approved RPO/RTO targets.

#### Scenario: Restore rehearsal succeeds
- **WHEN** a retained backup is restored to an isolated environment
- **THEN** data integrity and measured recovery objectives are recorded against the artifact/database revision

#### Scenario: Restore fails or exceeds objective
- **WHEN** restore fails or exceeds target
- **THEN** hosted launch is blocked and an incident/remediation record is opened

### Requirement: Tenant deletion and incidents are auditable
The operator MUST execute a documented tenant-data deletion process and retain incident evidence without exposing tenant secrets.

#### Scenario: Tenant deletion requested
- **WHEN** an authorized deletion request is accepted
- **THEN** active data is tombstoned and deletion/backup-expiry progress is auditable

## Traceability

| Requirement | Proposal | Design | Boundary | Scenarios | Tasks | Verification oracle |
|---|---|---|---|---|---|---|
| R1 Safe release | staged hosted rollout | immutable artifact/readiness gate | CI/deployment; D1 | ready/fail | B1,D1,C1,V1 | staging rollout evidence |
| R2 Recovery | backup/restore | encrypted backup/RPO/RTO | Operations/DB; D3 | restore pass/fail | B2,D3,C1,V2 | isolated restore rehearsal |
| R3 Deletion/incident | accountable ops | tombstone/retention/runbook | Operations; D2,D4 | deletion | B1,D2,D4,C2,V2 | audit and expiry evidence |
