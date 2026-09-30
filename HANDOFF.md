current_spec: package-adoption-metrics

# Handoff

## State

The first OpenSpec change `github-portfolio-momentum-mvp` is implemented, verified, archived and committed (a0332cb + 3f5a7f8). The second change `package-adoption-metrics` is now implemented, verified, archived and committed (2c4a89d). Argoscope is no longer planning-only: the .NET 10 API, React/TypeScript web, EF Core persistence, daily collection hosted services, fake and read-only REST GitHub providers, velocity/acceleration/engagement/benchmark/score analytics, owner-managed package adoption across DockerHub/npm/NuGet/PyPI/crates.io, and the docs screenshots are all running end-to-end against synthetic data with zero live GitHub or package-registry calls. GitHub repository `lileililiwen/argoscope` was published earlier and remains `metadata_verified`.

## Active change

`openspec/changes/package-adoption-metrics/` is now archived under `openspec/changes/archive/2026-09-30-package-adoption-metrics/` and produced `openspec/specs/package-adoption/spec.md` (4 requirements: owner-confirmed association, source-attributed observation semantics, last-good retention on failure, adoption series coverage disclosure). The two prior roadmap changes (`github-portfolio-momentum-mvp`, archived) and the remaining six (`commercial-signal-review`, `hosted-billing`, `hosted-multiuser-identity`, `hosted-operations`, `portfolio-attention-alerts`, `portfolio-decision-journal`) are unblocked and follow the same workflow.

## Verification evidence

- Implementation commit: 2c4a89d `Implement package-adoption-metrics`.
- `dotnet format --verify-no-changes`: PASS (only pre-existing CA1848 / CA1707 / xUnit2013 analyzer warnings without code fixers).
- `dotnet build Argoscope.sln`: 0 errors.
- `dotnet test Argoscope.sln`: 87 Argoscope.UnitTests + 2 Argoscope.IntegrationTests PASS. Added 4 unit-test files (`PackageCoordinateValidatorTests`, `PackageAdoptionAggregatorTests`, `PackageCollectionServiceTests`, `IdJsonConverterTests`) + `InMemoryPackageStores` test helper, and one end-to-end integration test `PackageAdoption_LinkCollectAndFetch_RejectsInvalidCoordinate`. The integration test exercises the full pipeline via `WebApplicationFactory<Program>` with the shared InMemory database, the per-provider `FakePackageMetricsProvider`, and the new `IdJsonConverter` so `Id<T>` payload values appear as bare Guid strings.
- `npm run build` (web → src/Argoscope.Api/wwwroot): PASS; 42 modules transformed, output ~188 kB JS / ~3 kB CSS.
- `npm test` (Vitest + jsdom): 3/3 PASS.
- `openspec validate --all --strict --no-interactive`: 8/8 PASS (the two archived specs and the six remaining roadmap changes).
- `python3 scripts/verify_bootstrap.py`: PASS.
- `openspec archive package-adoption-metrics --yes`: archived to `openspec/changes/archive/2026-09-30-package-adoption-metrics/`; generated `openspec/specs/package-adoption/spec.md`; the `Purpose:` section was written post-archive (no TBD).
- API smoke run (ASPNETCORE_ENVIRONMENT=Screenshot, ASPNETCORE_URLS=http://127.0.0.1:5174, no `GitHub:Token` → fake provider, no live package-registry call → per-provider `FakePackageMetricsProvider` registered in place of the stub HTTP adapters, InMemory database, `Seed` config present → `ScreenshotSeedHostedService` seeds four synthetic repositories, four owner-managed package associations and runs an initial collection pass for both GitHub and packages): 4× `collection for {owner}/{name}: snapshots=122-123 repoStatus=Available metricsStatus=Available engagementStatus=Available` and 4× `package collection for {provider}/{coordinate}: written=30 preserved=0 meta=Available page=Available`. SPA fallback serves `index.html` for any non-API GET, so the React router takes over.
- `shot-scraper http://127.0.0.1:5174/portfolios/<id>/overview?window=30d --output docs/assets/README.jpg --width 1440 --height 900 --wait 3000 --quality 85`: wrote 96 792-byte JPEG of the real rendered dashboard. `shot-scraper http://127.0.0.1:5174/repositories/<id>/adoption --output docs/assets/adoption.jpg --width 1440 --height 900 --wait 3000 --quality 85`: wrote 93 348-byte JPEG of the per-repository adoption page. Privacy review: PASS — synthetic names only, no real owner logins or repository identities, no token configured, no live GitHub call, no live package-registry call; details in `docs/assets/capture-plan.md`.
- Package adoption rules: coordinates are validated per provider (DockerHub `namespace/name`, npm `@scope/name` or unscoped, NuGet case-insensitive, PyPI lowercase, crates.io preserves casing); observations are idempotent on `(association, unit, window, window start, window end)`; a transient provider failure leaves the association untouched and preserves the prior complete row; not-found flips the association to `AttentionRequired` and a later successful run recovers to `Linked`; unlike units are never combined into a single series.
- No mutation endpoints reach GitHub or any package registry. The real `GitHubRepositoryProvider` only performs GETs; metric/engagement page methods return `ProviderResultStatus.Unavailable` with diagnostic code `github-rest-not-implemented` for this MVP. The package `StubHttpPackageMetricsProvider` returns `Unavailable` with `provider-not-implemented`. All tests and the screenshots use the fakes.
- Missing data is reported as unavailable (insufficient reason, status pill, `—` rendering) — never as zero. Owner vs external activity remains split into separate `EngagementSummary` buckets; the package adoption series report carries its own per-series `Status`, `Coverage`, expected/actual point counts and an `InsufficientReason` for the dashboard.

## Next actions

1. Pick the next active change from the six remaining roadmap packages. The natural dependency order after `package-adoption-metrics` is `portfolio-attention-alerts` (depends only on MVP) or `portfolio-decision-journal` (depends only on MVP); `commercial-signal-review` is also independent of the package work.
2. Reuse the same BFS → DFS → BFS workflow, fake provider for tests, EF stores, and OpenSpec archive + two-commit pattern.
3. After completing each subsequent change, regenerate `docs/assets/README.jpg` and `docs/assets/adoption.jpg` if the dashboard or adoption surface changes, and re-record the privacy review in `docs/assets/capture-plan.md`.
4. Do not introduce dependencies on `dotnet-platform-libs` until a published package compatible with .NET 10, EF Core 10 and the chosen hosting model exists; prefer the local thin adapters and direct EF Core + Npgsql used here.
5. The dotnet-platform-libs shared checkpoint in `ROADMAP.md` and the `docs/architecture/platform-evaluation.md` decision record both still apply.
