# Tenant-data deletion workflow

Documented privacy workflow with auditable progress. Deletion failure
stays visible and is retried with audit.

## Lifecycle

1. **Request** (owner-only): `POST /api/v1/tenants/{id}/deletion`.
   The tenant is tombstoned immediately — hosted reads stop returning
   its data — and the request records `requestedBy`, `requestedAt`,
   `activePurgeDueAt` (+30 days) and `backupExpiryDueAt` (+30 days).
2. **Active purge** (operator, within 30 days):
   `POST /api/v1/tenants/{id}/deletion/active-purge` removes live
   database rows for the tenant. Progress is observable at
   `GET /api/v1/tenants/{id}/deletion` (`Tombstoned` → `ActivePurged`).
3. **Backup expiry** (operator, within retention window):
   `POST /api/v1/tenants/{id}/deletion/backup-expiry` expires backup
   copies (`ActivePurged` → `BackupExpired`).

## Rules

- Steps are ordered: backup expiry before active purge is rejected
  (409), duplicate requests while one is in flight are rejected (409).
- Cross-tenant reads return 404 with no disclosure; non-owners get 403;
  anonymous callers get 401.
- A failed purge/expiry leaves the request in its current state with the
  due dates visible; the operator retries and the audit trail shows the
  delay. Nothing is silently dropped.
