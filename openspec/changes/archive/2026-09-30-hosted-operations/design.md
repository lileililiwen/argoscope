# Design: Hosted operations readiness

## Implementation boundary
Argoscope repository deployment and operations surfaces: production container/deployment manifests, CI release workflow, ASP.NET Core 10 health/telemetry configuration, PostgreSQL migration and backup tooling, operator runbooks. No feature-domain schema changes except operational metadata and no sibling edits.

## Language and runtime
.NET 10 service in Linux containers, PostgreSQL, Hangfire, React/TypeScript frontend. IaC/deployment language follows selected host platform; provider selection is a blocker before authoring vendor-specific manifests. Verify .NET/frontend CI, container scan/build, migration rehearsal, backup restore and strict OpenSpec validation.

## Ownership and shared code
Argoscope owns service SLO, data retention and recovery procedures; infrastructure provider owns platform primitives. Reuse dotnet-platform-libs only for compatible published health/persistence packages. No production dependency on the user's desktop/workspace.

## Behavioral model
Release pipeline validates artifact, applies backward-compatible expand migration, deploys, checks health, then contracts schema only in a later release. Database backups encrypted in transit/at rest, daily full plus point-in-time recovery where supported, 30-day retention. Target baseline SLO: 99.5% monthly API availability; RPO 24h, RTO 8h until a host-specific rehearsal demonstrates stricter values. Health separates liveness and readiness; readiness checks database and job queue without exposing secrets. Critical events page operator through configured external monitoring; user-facing status records incident window and scope. Tenant data deletion workflow tombstones immediately, removes active DB data within 30 days, and expires backup copies within retention window.

## Contract and compatibility
Health endpoints expose status components only, no connection strings. Deployment config injects secrets from selected host secret manager; no `.env`/secret in image. Release artifact is immutable and tagged by source revision. Migration execution uses a single lock and aborts deployment on failure. Operator runbooks specify access control, audit retention, incident severity, notification, restore and post-incident review. Hosting vendor remains an implementation blocker; no vendor-specific assumption is encoded.

## Failure and boundary policy
Readiness failure blocks traffic rollout; failed migration rolls back application release and leaves expand-compatible schema; backup verification failure blocks release; restore is isolated before promotion; provider outage triggers incident procedure; data deletion failure remains visible and retried with audit; liveness does not restart on dependency-only outage.

## Verification oracle
CI deploys to isolated staging and demonstrates migration from previous release, health checks, secret absence, backup restore to isolated database, RPO/RTO measurement, tenant deletion lifecycle, alerting/incident exercise and artifact provenance. Readiness is not complete until named operator signs recorded evidence. Build/test/strict validation plus rehearsal evidence required.

## Decision ledger
- Hosting provider and region must be selected before vendor-specific implementation; owner is the project operator.
- 99.5% SLO, 24h RPO, 8h RTO and 30-day backup retention are initial targets, not guarantees; operator must approve based on measured cost/capability.
- Hosted launch is blocked until identity and billing changes are complete and operational rehearsal passes.
