current_spec: package-adoption-metrics

# Handoff

## State

The first OpenSpec change `github-portfolio-momentum-mvp` is implemented, verified, archived and committed (a0332cb). A second HANDOFF-only commit follows immediately to record this evidence. Argoscope is no longer planning-only: the .NET 10 API, React/TypeScript web, EF Core persistence, daily collection hosted service, fake and read-only REST GitHub providers, velocity/acceleration/engagement/benchmark/score analytics, and the docs screenshot are all running end-to-end against synthetic data with zero live GitHub calls. GitHub repository `lileililiwen/argoscope` was published earlier and remains `metadata_verified`.

## Active change

`openspec/changes/package-adoption-metrics/` is the next active OpenSpec change (phase-5, no dependency on the other two phase-5 packages). The previous change lives in `openspec/changes/archive/2026-09-30-github-portfolio-momentum-mvp/` and produced `openspec/specs/portfolio-analytics/spec.md` (6 requirements: portfolio + repository identity, GitHub collection + snapshot, growth/acceleration/engagement, peer benchmarks, priority score, dashboard).

## Verification evidence

- Implementation commit: a0332cb `Implement github-portfolio-momentum-mvp` (85 files, +9229/-34).
- `dotnet format --verify-no-changes`: PASS.
- `dotnet build Argoscope.sln`: 0 errors (only CA1707 test-naming + CA1848 LoggerMessage analyzer suggestions, both non-fatal and without code fixers).
- `dotnet test Argoscope.sln`: 26 Argoscope.UnitTests + 1 Argoscope.IntegrationTests PASS. Integration test exercises the full pipeline via `WebApplicationFactory<Program>` with the shared InMemory database and the deterministic `FakeGitHubRepositoryProvider`.
- `npm run build` (web → src/Argoscope.Api/wwwroot): PASS; 41 modules transformed, output ~183 kB JS / ~2.8 kB CSS.
- `npm test` (Vitest + jsdom): 3/3 PASS.
- `openspec validate --all --strict --no-interactive`: 8/8 PASS (the archived spec and the seven remaining roadmap changes).
- `python3 scripts/verify_bootstrap.py`: PASS (script updated to also recognize the just-archived change directory so commit-time verification keeps working immediately after `openspec archive`).
- `openspec archive github-portfolio-momentum-mvp --yes`: archived to `openspec/changes/archive/2026-09-30-github-portfolio-momentum-mvp/`; generated `openspec/specs/portfolio-analytics/spec.md`; the `Purpose:` section was written post-archive (no TBD).
- API smoke run (ASPNETCORE_ENVIRONMENT=Screenshot, ASPNETCORE_URLS=http://127.0.0.1:5174, no `GitHub:Token` → fake provider, InMemory database, `Seed` config present → `ScreenshotSeedHostedService` seeds four synthetic repositories and runs an initial collection pass): 4× `snapshots=122-123 repoStatus=Available metricsStatus=Available engagementStatus=Available`. SPA fallback now serves `index.html` for any non-API GET, so the React router takes over.
- `shot-scraper http://127.0.0.1:5174/portfolios/<id>/overview?window=30d --output docs/assets/README.jpg --width 1440 --height 900 --wait 2500 --quality 85`: wrote 96 768-byte JPEG of the real rendered dashboard. Privacy review: PASS — synthetic names only, no real owner logins or repository identities, no token configured, no live GitHub call; details in `docs/assets/capture-plan.md`.
- No mutation endpoints reach GitHub. The real `GitHubRepositoryProvider` only performs GETs; metric/engagement page methods return `ProviderResultStatus.Unavailable` with diagnostic code `github-rest-not-implemented` for this MVP. All tests and the screenshot use the fake provider.
- Missing data is reported as unavailable (insufficient reason, status pill, `—` rendering) — never as zero. Owner vs external activity is split into separate `EngagementSummary` buckets; the spec's owner-vs-external attribution rule is preserved through the collection, aggregation and dashboard layers.

## Next actions

1. Implement the next active change `package-adoption-metrics` (independent of the other phase-5 packages). Reuse the same BFS → DFS → BFS workflow, fake provider for tests, EF stores, and OpenSpec archive + two-commit pattern.
2. After completing each subsequent change, regenerate `docs/assets/README.jpg` if the dashboard surface changes and re-record the privacy review in `docs/assets/capture-plan.md`.
3. Do not introduce dependencies on `dotnet-platform-libs` until a published package compatible with .NET 10, EF Core 10 and the chosen hosting model exists; prefer the local thin adapters and direct EF Core + Npgsql used here.
4. The dotnet-platform-libs shared checkpoint in `ROADMAP.md` and the `docs/architecture/platform-evaluation.md` decision record both still apply.
