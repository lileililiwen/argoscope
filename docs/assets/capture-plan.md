# Documentation capture plan

- **Purpose:** capture the real Argoscope portfolio dashboard for README documentation.
- **Source:** local app route http://127.0.0.1:<port>/ using synthetic repository observations.
- **Command/settings:** shot-scraper http://127.0.0.1:5174/portfolios/<id>/overview?window=30d --output docs/assets/README.jpg --width 1440 --height 900 --wait 2500 --quality 85. App started with ASPNETCORE_ENVIRONMENT=Screenshot so the in-memory `Seed` config seeds four synthetic repositories (example/demo-app, example/ingest-pipeline, competitor-a/rival-monitor, competitor-b/scout-board) and runs an initial collection pass.
- **Artifact:** docs/assets/README.jpg (regenerated 2026-09-30; 1440×900 JPEG, quality 85).
- **Revision/time:** capture taken against bootstrap revision that implemented github-portfolio-momentum-mvp; capture timestamp 2026-09-30T10:19Z.
- **Privacy review:** PASSED. The capture uses only synthetic repository names and metric data declared in src/Argoscope.Api/appsettings.Screenshot.json. No live GitHub call is performed: the fake provider is used, no token is configured, and the daily hosted service is disabled. No real owner logins, real repository identities, real metric values, or traffic details appear in the screenshot. The as-of timestamp in the UI is server time and does not reveal any real data.
- **Status:** DONE. The active OpenSpec change produced a runnable application; the screenshot above is the genuine rendered dashboard. Future changes to the UI must regenerate this artifact and re-record the timestamp and privacy review.
