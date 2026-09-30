# Argoscope product brief

## Purpose

Argoscope is an open-source portfolio analytics dashboard for GitHub projects. It answers: “Which of my projects deserve limited development time and infrastructure?” The core object is a portfolio containing owned projects and related competitor repositories.

## MVP

- Portfolio and repositories with explicit owned/competitor/category roles and lifecycle labels (Idea, Prototype, Open Source, Growing, Validated, SaaS Candidate, Hosted, Maintenance, Archived). The owner controls lifecycle; analytics may suggest attention but never change it.
- GitHub PAT-based self-hosted access. Collect public repository metadata and owner-authorized fields only when permissions allow. OAuth hosted accounts are deferred.
- Daily snapshots for stars, forks, watchers, open issues, open PRs, contributor count, commits, releases, languages, repository age and last activity. Preserve capture time/source and distinguish unavailable from zero.
- Star/fork velocity over 7 and 30 days; acceleration compares the current 30-day change with the previous 30-day change. Engagement reports external issues/PRs/contributors separately from owner activity.
- Competitor comparison for 30-day growth, fork/star, contributors, external issues/PRs and release velocity; category median only when cohort/data coverage is sufficient.
- Transparent, configurable priority score with named factors, weights, missing-factor behavior and evidence coverage. Defaults follow the brief: momentum 30%, engagement 25%, adoption 20%, external users 15%, commercial 10%; factors without MVP data are marked unavailable and are not silently set to zero.

## Deferred

Traffic/views/clones (owner permission-bound), package downloads (Docker Hub, npm, NuGet, PyPI, crates.io), AI issue/commercial classification, alerts, decision journal, hosted OAuth accounts, billing, SaaS hosting, and automatic lifecycle transitions.

## Evidence and trust rules

Every snapshot has source, observed-at UTC, request window, permissions/coverage and freshness. Do not fabricate time series by backfilling current counts. GitHub rate limits and permission denials are visible. Separate owner actions from external community engagement. A star is an interest signal, not proof of usage. Package downloads/installs and commercial signals need independent providers and their own evidence.

## Stack and status

Planned: ASP.NET Core 10, React + TypeScript, PostgreSQL, Hangfire, GitHub GraphQL and REST APIs, Docker. Self-hosted single owner first. This is a planning baseline; there is no runtime or GitHub data integration yet.
