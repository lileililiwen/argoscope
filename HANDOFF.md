current_spec: portfolio-attention-alerts

# Handoff

## State

The first OpenSpec change `github-portfolio-momentum-mvp` is implemented, verified, archived and committed (a0332cb + 3f5a7f8). The second change `package-adoption-metrics` is implemented, verified, archived and committed (2c4a89d + eadac1a). The third change `portfolio-decision-journal` is implemented, verified, archived and committed (2065f44 + 6fc0c95). The fourth change `portfolio-attention-alerts` is now implemented, verified, archived and committed (dec4696). Argoscope is no longer planning-only: the .NET 10 API, React/TypeScript web, EF Core persistence, daily collection hosted services, fake and read-only REST GitHub providers, velocity/acceleration/engagement/benchmark/score analytics, owner-managed package adoption across DockerHub/npm/NuGet/PyPI/crates.io, owner-authored decision journal with append-only revisions and scoped evidence links, per-portfolio attention alerts with allowlisted thresholds plus deduplicated bounded delivery, and the docs screenshots are all running end-to-end against synthetic data with zero live GitHub, package-registry or delivery calls. GitHub repository `lileililiwen/argoscope` was published earlier and remains `metadata_verified`.

## Active change

`openspec/changes/portfolio-attention-alerts/` is now archived under `openspec/changes/archive/2026-09-30-portfolio-attention-alerts/` and produced `openspec/specs/portfolio-alerts/spec.md` (3 requirements: allowlisted rule/coverage gate, bounded deduplicated delivery, destination/secret protection). The four archived changes (`github-portfolio-momentum-mvp`, `package-adoption-metrics`, `portfolio-decision-journal`, `portfolio-attention-alerts`) and the remaining four (`commercial-signal-review`, `hosted-billing`, `hosted-multiuser-identity`, `hosted-operations`) are unblocked and follow the same workflow.

## Verification evidence

- Implementation commit: dec4696 `Implement portfolio-attention-alerts`.
- `dotnet format --verify-no-changes` on all new/modified alert files: PASS (exit 0). Full-solution verify flags 4 pre-existing files (`PackageCollectionService.cs`, `GitHubRepositoryProvider.cs`, `DailyCollectionHostedService.cs`, `PackageCollectionHostedService.cs`, untouched since a0332cb) that want CA1848 LoggerMessage formatting; no alert file is affected.
- `dotnet build Argoscope.sln`: 0 errors.
- `dotnet test Argoscope.sln`: 121 Argoscope.UnitTests + 5 Argoscope.IntegrationTests PASS. Added `AlertEvaluatorTests` (threshold equality, missing/low-coverage gating, cooldown), `WebhookSafetyTests` (HTTP/loopback/private/link-local rejection), `DeliveryPayloadSignerTests` (HMAC determinism), `AlertServiceTests` (unsafe-metric/webhook rejection, fire-once + dedupe + masking + HMAC, missing-metric skip, transient retry with backoff, stale-version conflict + soft-delete) with `InMemoryAlertStores`/`InMemoryEngagementStore` helpers, and extended `ApiEndToEndTests` with `AlertRules_CreateEvaluateHistory_MasksSecretsAndRejectsUnsafe`.
- `npm run build` (web → src/Argoscope.Api/wwwroot): PASS; 44 modules transformed, output ~206 kB JS / ~3 kB CSS.
- `npm test` (Vitest + jsdom): 3/3 PASS.
- `openspec validate --all --strict --no-interactive`: 8/8 PASS (four archived specs plus four remaining roadmap changes).
- `python3 scripts/verify_bootstrap.py`: PASS.
- `openspec archive portfolio-attention-alerts --yes`: archived to `openspec/changes/archive/2026-09-30-portfolio-attention-alerts/`; generated `openspec/specs/portfolio-alerts/spec.md`; the `Purpose:` section was written post-archive (no TBD).
- API smoke run (ASPNETCORE_ENVIRONMENT=Screenshot, ASPNETCORE_URLS=http://127.0.0.1:5174, no `GitHub:Token` → fake provider, per-provider `FakePackageMetricsProvider`, InMemory database, `Seed` config → seeded portfolio + collection pass): curl POST created Email rule `Stars momentum watch` (stars_30d ≥ 5, masked `o***@example.com`, `hasSecret=false`); unsafe `http://` webhook POST → 400 with no request sent; evaluate POST → `Fired` with metricValue 5185, coverage 1, one `PermanentFailure/email-not-configured` attempt; history GET lists the evaluation; rules list contains no raw email address.
- `shot-scraper http://127.0.0.1:5174/portfolios/<id>/alerts --output docs/assets/alerts.jpg --width 1440 --height 900 --wait 3000 --quality 85`: wrote 89 455-byte JPEG of the alerts page (rule form + masked rule list + evaluation/delivery history). Privacy review: PASS — synthetic seed data only, masked destination, no live GitHub/package/delivery calls; details in `docs/assets/capture-plan.md`.
- Alert rules: allowlist `stars_7d`, `stars_30d`, `external_engagement_30d`, `momentum_score`, `snapshot_staleness_hours`; evaluation key `(ruleId, metricWindowEndUtc day)` dedupes retried jobs; cooldown blocks repeat fires; missing/under-covered metrics record `SkippedInsufficientData` with no delivery; webhook HTTPS-only with per-send DNS + per-IP SSRF gate (loopback/private/link-local/multicast rejected, fail-closed); secrets write-only and masked (`o***@example.com`, `https://host/***`); delivery `Queued → Sending → Delivered|RetryableFailure|PermanentFailure`, max 5 attempts with quadratic backoff, 429/5xx retryable vs other 4xx permanent, email-unconfigured recorded as visible permanent failure.
- Prior evidence retained: decision-journal (2065f44; 97 unit + 4 integration at that revision; decisions.jpg); package-adoption (2c4a89d; README.jpg 96 792-byte, adoption.jpg 93 348-byte).

## Next actions

1. Pick the next active change from the four remaining roadmap packages. The natural next step is `commercial-signal-review` (depends only on MVP, independent of alerts); the hosted chain (`hosted-multiuser-identity` → `hosted-billing` → `hosted-operations`) follows after.
2. Reuse the same BFS → DFS → BFS workflow, fake provider for tests, EF stores, and OpenSpec archive + two-commit pattern.
3. After completing each subsequent change, regenerate `docs/assets/README.jpg` and `docs/assets/adoption.jpg` if the dashboard or adoption surface changes, and re-record the privacy review in `docs/assets/capture-plan.md`.
4. Do not introduce dependencies on `dotnet-platform-libs` until a published package compatible with .NET 10, EF Core 10 and the chosen hosting model exists; prefer the local thin adapters and direct EF Core + Npgsql used here.
5. The dotnet-platform-libs shared checkpoint in `ROADMAP.md` and the `docs/architecture/platform-evaluation.md` decision record both still apply.
