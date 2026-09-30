# Argoscope change workflow

Use OpenSpec for non-trivial changes. Follow BFS → DFS → BFS:

1. **BFS baseline:** map callers, GitHub scopes/rate limits, data ownership, snapshot freshness, schema/migrations, ranking consumers, privacy, and failure boundaries.
2. **DFS implementation:** implement one requirement/scenario at a time; use a fake GitHub provider for deterministic tests. Persist source and observation time. Keep GitHub tokens in deployment secret storage only.
3. **BFS regression:** re-audit REST/GraphQL callers, pagination, partial failures, rate-limit resets, deleted/private repos, UTC windows, owner/external activity separation, rank coverage, and UI disclosure.
4. **Verification:** run local formatting/build/tests, strict OpenSpec validation and applicable Gate before archive. Record exact blocked commands and next actions; build/test/skeleton success is not runtime evidence.

Do not log or commit tokens, clone URLs containing credentials, private repository names outside local authorized use, or traffic details. Do not infer adoption from stars; show missing metrics as unavailable, not zero. A ranking is a decision aid, not an automatic project lifecycle decision.

Each completed OpenSpec spec/change requires exactly two commits: first the implementation/tests/archive commit, then a HANDOFF.md-only pointer/evidence commit. Do not ask for conversational authorization or confirmation before either commit when the change is already authorized. After commit 2, stop.

Initialization additionally requires a successful gh auth status, preserved/new origin, public repo publication without force-push, applied description/homepage/topics, and gh repo view plus git ls-remote --heads origin main verification.
