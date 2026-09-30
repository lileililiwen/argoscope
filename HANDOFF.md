current_spec: portfolio-decision-journal

# Handoff

## State

The first OpenSpec change `github-portfolio-momentum-mvp` is implemented, verified, archived and committed (a0332cb + 3f5a7f8). The second change `package-adoption-metrics` is implemented, verified, archived and committed (2c4a89d + eadac1a). The third change `portfolio-decision-journal` is now implemented, verified, archived and committed (2065f44). Argoscope is no longer planning-only: the .NET 10 API, React/TypeScript web, EF Core persistence, daily collection hosted services, fake and read-only REST GitHub providers, velocity/acceleration/engagement/benchmark/score analytics, owner-managed package adoption across DockerHub/npm/NuGet/PyPI/crates.io, owner-authored decision journal with append-only revisions and scoped evidence links, and the docs screenshots are all running end-to-end against synthetic data with zero live GitHub or package-registry calls. GitHub repository `lileililiwen/argoscope` was published earlier and remains `metadata_verified`.

## Active change

`openspec/changes/portfolio-decision-journal/` is now archived under `openspec/changes/archive/2026-09-30-portfolio-decision-journal/` and produced `openspec/specs/decision-journal/spec.md` (3 requirements: revision history, scoped evidence links, no automation). The three prior archived changes (`github-portfolio-momentum-mvp`, `package-adoption-metrics`, `portfolio-decision-journal`) and the remaining five (`commercial-signal-review`, `hosted-billing`, `hosted-multiuser-identity`, `hosted-operations`, `portfolio-attention-alerts`) are unblocked and follow the same workflow.

## Verification evidence

- Implementation commit: 2065f44 `Implement portfolio-decision-journal`.
- `dotnet format --verify-no-changes`: PASS (only pre-existing CA1848 analyzer warnings without code fixers).
- `dotnet build Argoscope.sln`: 0 errors, 0 warnings.
- `dotnet test Argoscope.sln`: 97 Argoscope.UnitTests + 4 Argoscope.IntegrationTests PASS. Added `DecisionServiceTests` + `InMemoryDecisionStores`/`InMemoryCommonStores` helpers and extended `ApiEndToEndTests` with decision create/conflict/evidence coverage.
- `npm run build` (web → src/Argoscope.Api/wwwroot): PASS; 43 modules transformed, output ~198 kB JS / ~3 kB CSS.
- `npm test` (Vitest + jsdom): 3/3 PASS.
- `openspec validate --all --strict --no-interactive`: 8/8 PASS (three archived specs plus five remaining roadmap changes).
- `python3 scripts/verify_bootstrap.py`: PASS.
- `openspec archive portfolio-decision-journal --yes`: archived to `openspec/changes/archive/2026-09-30-portfolio-decision-journal/`; generated `openspec/specs/decision-journal/spec.md`.
- Decisions screenshot `docs/assets/decisions.jpg` (65 390-byte JPEG) added; capture plan re-recorded with privacy review PASS (synthetic data only, no live calls); details in `docs/assets/capture-plan.md`.
- Decision rules: entry create/edit appends ordered revisions with optimistic concurrency (stale revision → 409, history unchanged); soft-delete/restore preserved; evidence references validated same-portfolio and marked `Unresolved` when later unavailable; journal writes never alter scores, lifecycle state, or external repositories.
- Prior package-adoption evidence retained: implementation commit 2c4a89d; 87 unit + 2 integration tests at that revision; README.jpg (96 792-byte) and adoption.jpg (93 348-byte) synthetic captures with privacy PASS.

## Next actions

1. Pick the next active change from the five remaining roadmap packages. The natural dependency order after `portfolio-decision-journal` is `portfolio-attention-alerts` (depends on MVP + phase-5 packages for trigger scope); `commercial-signal-review` is also independent of alerts.
2. Reuse the same BFS → DFS → BFS workflow, fake provider for tests, EF stores, and OpenSpec archive + two-commit pattern.
3. After completing each subsequent change, regenerate `docs/assets/README.jpg` and `docs/assets/adoption.jpg` if the dashboard or adoption surface changes, and re-record the privacy review in `docs/assets/capture-plan.md`.
4. Do not introduce dependencies on `dotnet-platform-libs` until a published package compatible with .NET 10, EF Core 10 and the chosen hosting model exists; prefer the local thin adapters and direct EF Core + Npgsql used here.
5. The dotnet-platform-libs shared checkpoint in `ROADMAP.md` and the `docs/architecture/platform-evaluation.md` decision record both still apply.
