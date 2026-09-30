# Argoscope completion rules

Complete a requirement only when success/failure/boundary scenarios have observable evidence; API scopes, pagination and rate limits are reviewed; persistence migrations and callers are checked; deterministic calculations and tests pass; project verification and the shared governance Gate pass; strict OpenSpec validation passes; the change is archived; and exactly two commits for the change are recorded.

Every metric includes source and observed-at time. Calculated growth uses complete comparable windows and explicitly handles missing/zero baselines. External engagement excludes owner actions. Rankings show active factors, weights, data coverage and unavailable values. Never claim adoption or commercial intent from stars alone.

Never claim build, tests, GitHub API, runtime, screenshot, release or deployment evidence that was not observed. Record blocked checks and the next action in HANDOFF.md.

Each completed OpenSpec spec/change requires exactly two commits: first the implementation/tests/archive commit, then a HANDOFF.md-only pointer/evidence commit.
