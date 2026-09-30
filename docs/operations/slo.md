# Hosted service objectives

Status: initial targets, not guarantees. The operator must approve each
value against measured cost/capability before production; hosting vendor
and region selection is still an explicit blocker.

| Objective | Target | Source |
|---|---|---|
| Monthly API availability | 99.5% | `Operations: SloAvailabilityPercent` |
| Recovery-point objective (RPO) | 24 h | `Operations: RpoHours` |
| Recovery-time objective (RTO) | 8 h | `Operations: RtoHours` |
| Encrypted backup retention | 30 days | `Operations: BackupRetentionDays` |
| Tenant active-data purge after deletion | 30 days | `Operations: ActivePurgeDays` |
| Hosting region | unselected | `Operations: Region` (operator decision) |
| Launch sign-off | named operator required | `Operations: RequireOperatorSignoff` |

## Rules

- No availability is claimed without measured evidence from the production
  host. The 99.5% figure is a baseline to plan monitoring against.
- RPO/RTO are demonstrated by isolated restore rehearsals recorded at
  `POST /api/v1/ops/restore-rehearsals`; a failed or over-objective
  rehearsal blocks launch and opens a remediation incident.
- Tightening any target requires a new rehearsal proving it.
