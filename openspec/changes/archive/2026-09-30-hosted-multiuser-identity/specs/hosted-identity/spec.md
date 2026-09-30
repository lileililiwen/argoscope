# hosted-identity Specification

## Purpose
Provide authenticated multi-user access with enforced tenant isolation for hosted Argoscope.

## ADDED Requirements

### Requirement: Tenant ownership is mandatory for hosted data
Every tenant-owned portfolio resource and asynchronous job MUST be scoped to an authenticated tenant, with no global fallback.

#### Scenario: Access own tenant resource
- **WHEN** an active member requests a resource owned by their tenant
- **THEN** authorization applies the member role and returns only that tenant's data

#### Scenario: Access another tenant resource
- **WHEN** a member requests a resource owned by another tenant
- **THEN** the API returns not found and discloses no resource data

### Requirement: Membership roles enforce least privilege
The system MUST enforce Owner, Editor and Viewer capabilities and preserve at least one active Owner in each tenant.

#### Scenario: Viewer attempts mutation
- **WHEN** a Viewer submits a portfolio mutation
- **THEN** the request is forbidden and data is unchanged

#### Scenario: Remove the last owner
- **WHEN** an operation would leave no active Owner
- **THEN** the operation is rejected and membership state remains unchanged

### Requirement: Existing data is migrated before hosted access
Hosted rollout MUST assign all existing single-owner data to an explicit tenant and verify complete tenant scoping before serving hosted requests.

#### Scenario: Migration encounters invalid ownership
- **WHEN** a legacy row cannot be assigned or validation finds an unscoped resource
- **THEN** migration fails atomically and hosted access remains disabled

## Traceability

| Requirement | Proposal | Design | Boundary | Scenarios | Tasks | Verification oracle |
|---|---|---|---|---|---|---|
| R1 Tenant isolation | tenant scoped access | mandatory tenant context | All API/data/jobs; D2 | own/cross tenant | B1,B2,D2,C1,V2 | two-tenant isolation matrix |
| R2 Roles/invariants | membership management | role matrix/last owner | Identity/Application; D1,D4 | viewer/last owner | B2,D1,D4,C2,V1 | authorization tests |
| R3 Safe migration | hosted readiness | transactional ownership migration | Persistence/deployment; D3 | migration failure | B1,B2,D3,C3,V1 | rollback and null-free migration test |
