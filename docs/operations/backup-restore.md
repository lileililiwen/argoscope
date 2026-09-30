# Backup and restore

Encrypted backups with isolated restore rehearsals against approved
RPO (24 h) / RTO (8 h) targets.

## Backup

- Daily full database backup plus point-in-time recovery where the host
  supports it; 30-day retention (`Operations: BackupRetentionDays`).
- Encrypted in transit and at rest using the host platform primitives
  (provider selection is an operator decision; no vendor assumption is
  encoded here).
- Backup access is audited; backup copies never leave the retention
  window. Tenant backup copies expire with the deletion workflow
  (`docs/operations/tenant-deletion.md`).
- Backup verification failure blocks the release (same gate as migration
  and readiness failures).

## Restore rehearsal

1. Restore a retained backup to an isolated database — never the
   production database, never by promoting the backup in place.
2. Verify data integrity (row counts, checksums, application smoke read).
3. Measure wall-clock RPO (newest recoverable transaction age) and RTO
   (restore + verification duration).
4. Record the evidence with the operator identity:
   `POST /api/v1/ops/restore-rehearsals` with artifact/database revision,
   start/finish timestamps, measured RPO/RTO hours and integrity flag.
   The service evaluates the record against RPO/RTO targets and marks
   `meetsObjectives`.
5. A failed or over-objective rehearsal blocks hosted launch and opens a
   remediation incident automatically (visible at
   `GET /api/v1/ops/incidents`).
6. Launch requires a named operator sign-off on a passing rehearsal for
   the exact artifact revision being promoted.
