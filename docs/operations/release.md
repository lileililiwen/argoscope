# Release procedure

Immutable revision-tagged artifacts with staged migrations and a
readiness gate. Traffic promotion stops when migration or readiness fails.

## Artifact

- Every release is a container image tagged `argoscope:<revision>` where
  `<revision>` is the source revision (`ARGOSCOPE_RELEASE_REVISION` at
  build time, assembly informational version as fallback).
- The running revision is observable at `GET /api/v1/health/live` and
  `GET /api/v1/ops/release`. The gate refuses artifacts with no revision.
- The frontend served from the image is the `web/` build for that same
  revision (see `Dockerfile` webbuild stage).

## Staged migration

1. Expand: deploy a backward-compatible schema change that the previous
   application revision can still run against.
2. Deploy the new application revision; run pending migrations under a
   single lock before the process serves traffic.
3. Contract (breaking cleanup) only in a later release, never the same one.
4. Migration failure aborts the deployment; the prior compatible
   application remains available. Never promote traffic past a failed
   migration.

## Readiness gate

- Liveness `GET /api/v1/health/live` is dependency-free (process is up).
- Readiness `GET /api/v1/health/ready` checks database connectivity,
  expand-compatible migration state and job-queue status, and returns
  `503 not_ready` when any check fails. Responses expose status components
  only — no connection strings, secrets or tenant data.
- CI/production promotion runs `python3 scripts/ops_readiness.py
  --base-url <candidate> [--revision <expected>]`: exit 0 promotes,
  non-zero blocks and keeps the prior release.
- Liveness never restarts on a dependency-only outage; readiness alone
  blocks the rollout.
