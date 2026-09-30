# Argoscope

**An open-source portfolio analytics dashboard for GitHub projects.** Argoscope helps maintainers decide which owned projects and competitors deserve attention by tracking momentum, external engagement, and comparable trends.

> Status: planning and repository bootstrap only. No GitHub integration, app runtime, database schema, or analytics is implemented yet.

## Product boundary

Argoscope analyzes portfolios of repositories, not isolated star counters. Owners add repositories and competitors, collect timestamped GitHub metrics, compare growth against category peers, and rank attention using a transparent configurable formula. Metrics and rankings expose their inputs and data coverage.

The first usable release targets self-hosted single-owner deployment: ASP.NET Core 10, React + TypeScript, PostgreSQL, Hangfire, GitHub GraphQL/REST APIs, and Docker. Use an owner-supplied GitHub token for private owner analytics; public-repository data may be collected without elevated permissions. These are planned decisions, not implemented or runtime-verified facts.

## MVP

- Portfolio and repository/competitor membership management.
- Daily snapshots for stars, forks, watchers, issues, pull requests, contributors, releases, commits, languages, repository age and last activity, with visibility and permission status separated.
- 7/30-day velocity and 30-day acceleration from snapshots; engagement separates owner from external activity.
- Competitor comparisons and category medians with freshness and coverage shown.
- Transparent, configurable portfolio priority score; unavailable metrics are not silently treated as zero.
- GitHub rate-limit, permission, deleted/private repository, and stale snapshot states are visible.

## Deferred

Traffic/clones, package registries and downloads, commercial-intent AI, alerts, decision journal, hosted OAuth multi-user accounts, billing, SaaS hosting, and automatic lifecycle decisions are later work. See docs/product-brief.md and ROADMAP.md.

## Development

The application has not been scaffolded. Planned verification after implementation is dotnet build, dotnet test, npm ci, npm run build, npm test, strict OpenSpec validation, and the shared governance checker. This bootstrap does not claim any build, test, GitHub API, or runtime success.

## License

MIT. See LICENSE.
