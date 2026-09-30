# Operator runbook

Access control, incident severity, notification, restore and
post-incident review for the hosted service.

## Access control

- Production data access is owner-managed per tenant; operator access to
  backups and hosts is granted only through the host platform IAM with
  an audit trail. No shared credentials.
- Secrets (`GitHub:Token`, `Identity:Oidc:*`, `Billing:WebhookSecret`,
  database credentials) are injected from the host secret manager at
  deploy time. They never appear in images, logs, health responses or
  the repository. No `.env`/secret file ships in the image.

## Incident severity

| Severity | Meaning | Response |
|---|---|---|
| Critical | Service or data-loss risk | Page operator immediately, open incident, freeze releases |
| High | Objective missed / degraded | Acknowledge within 1 h, remediate before next release |
| Medium | Contained failure | Resolve within 1 business day |
| Low | Cosmetic / doc | Next scheduled window |

## Procedure

1. Open an incident: `POST /api/v1/ops/incidents` (owner-only) with
   title, severity, scope and summary. Critical events also page the
   operator through configured external monitoring — alert delivery for
   portfolio rules is not the service-health channel.
2. Acknowledge, mitigate, then resolve:
   `POST /api/v1/ops/incidents/{id}/resolve` with the resolution note.
3. User-facing status records the incident window and scope; incident
   bodies never carry tenant secrets.
4. Provider outage triggers this procedure; it never silently passes.

## Restore

Follow `docs/operations/backup-restore.md`: restore to an isolated
database first, verify integrity, measure RPO/RTO, record the rehearsal,
then promote. Never restore directly over production.

## Post-incident review

Within 5 business days of a Critical/High incident: timeline, root
cause, corrective actions with owners and due dates, and the rehearsal
or gate change that would have caught it. Retained with the incident
evidence.
