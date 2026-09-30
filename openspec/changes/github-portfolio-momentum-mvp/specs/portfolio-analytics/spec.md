# Portfolio analytics

## ADDED Requirements

### Requirement: Portfolios distinguish owned projects from competitors

The system MUST let an owner create a portfolio and associate repositories as owned or competitor, assign category and owner-controlled lifecycle metadata, and preserve stable GitHub repository identity across locator rename. Argoscope MUST NOT mutate the remote repository.

#### Scenario: Add owned and competitor repositories
- **WHEN** the owner adds two valid repository identities with distinct roles
- **THEN** the portfolio records both roles and displays repository identity and visibility status

#### Scenario: Reject duplicate membership
- **WHEN** the same stable repository identity is added to the same portfolio again
- **THEN** the system returns the existing membership and creates no duplicate

#### Scenario: Remote repository is renamed
- **WHEN** a later provider observation reports a new owner/name locator for the same stable node id
- **THEN** the system updates the display locator without creating a new repository identity or changing owner-selected role/lifecycle

### Requirement: GitHub collection records timestamped snapshots and coverage

The system MUST collect permitted GitHub metrics through a replaceable provider and persist values with source, metric period, collected-at UTC, completeness, and permission/provider status. It MUST NOT backfill history from present-day counters. Missing fields MUST remain unavailable/partial rather than become zero.

#### Scenario: Collect a complete daily snapshot
- **WHEN** the provider returns permitted metrics for a repository
- **THEN** one idempotent daily snapshot records each value and its observation/source metadata

#### Scenario: Provider returns only partial fields
- **WHEN** a collection response omits or cannot authorize one metric
- **THEN** verified fields are stored and omitted fields retain unavailable/partial status without overwriting a prior good value with zero

#### Scenario: Rate limit or permission failure
- **WHEN** the provider reports rate-limited, unauthorized, forbidden, or unavailable status
- **THEN** the system records classified status/checkpoint, retains last good snapshot, and retries only according to the bounded provider policy

### Requirement: Growth and acceleration use reproducible windows

The system MUST calculate 7/30-day velocity and 30-day acceleration only from timestamped comparable snapshots and return endpoint dates, elapsed/covered days, as-of time, and missing-data reasons. Zero baselines MUST produce null percent growth plus an explicit new-signal marker. The system MUST NOT invent pre-collection history.

#### Scenario: Calculate complete-window velocity
- **WHEN** complete snapshots exist at the selected window boundaries
- **THEN** the system returns the documented rate calculation and both source snapshot dates

#### Scenario: Insufficient snapshot coverage
- **WHEN** either required endpoint or preceding acceleration window is missing
- **THEN** the affected metric is null with an insufficient-coverage reason

#### Scenario: New repository starts from zero
- **WHEN** the baseline count is zero and a current count is positive
- **THEN** percent growth is null and the result marks a new signal rather than infinity

### Requirement: Engagement separates owner activity from external activity

The system MUST report external issues, pull requests, and contributor signals separately from owner-authored activity. Unknown author attribution MUST remain unknown and MUST NOT be counted as external.

#### Scenario: Compare owner and community activity
- **WHEN** a portfolio view displays engagement for a repository
- **THEN** owner and external counts are separate and include source window/as-of metadata

#### Scenario: Author cannot be classified
- **WHEN** provider identity is missing or ambiguous
- **THEN** the event is placed in unknown attribution and excluded from external totals

### Requirement: Competitor benchmarks expose cohort quality

The system MUST compare repositories using the same metric definition and time window. Category median MUST be returned only when at least five repositories have complete comparable values; otherwise the result MUST show insufficient cohort coverage.

#### Scenario: Cohort supports median
- **WHEN** five or more repositories have complete comparable metric windows
- **THEN** the system returns the median and cohort size with freshness/as-of evidence

#### Scenario: Cohort is too small
- **WHEN** fewer than five comparable repositories are available
- **THEN** the system omits the median and explains the minimum cohort condition

### Requirement: Priority ranking is transparent and coverage-aware

The system MUST calculate scores from a versioned configurable factor set, show factor values/weights/effective weights and coverage, and treat missing values as unavailable rather than zero. Ranking MUST be advisory and MUST NOT change owner-controlled lifecycle state.

#### Scenario: Rank with all MVP factors available
- **WHEN** valid configured factors exist for portfolio members
- **THEN** the system returns reproducible scores, factor values, weights, coverage and as-of time

#### Scenario: Rank while future factors are unavailable
- **WHEN** adoption or commercial factors have no provider data
- **THEN** the system excludes unavailable factors from effective normalization, reports reduced coverage, and does not label them zero

#### Scenario: Score weights are invalid
- **WHEN** an owner submits negative, non-finite or all-zero weights
- **THEN** the system returns a validation error and leaves the prior score configuration unchanged

## Traceability

| Requirement | Proposal | Design boundary | Tasks | Verification oracle |
|---|---|---|---|---|
| R1 Portfolio membership | Portfolio management | Domain/Application/API; stable node identity | D1 | CRUD/identity integration tests |
| R2 Snapshots | GitHub collection | GitHub provider + snapshot/checkpoint | D2–D3 | Fake provider pagination/partial/rate-limit tests; DB idempotency |
| R3 Growth | Growth metrics | Pure application metric service | D4 | Fixed UTC endpoint/zero/missing-window assertions |
| R4 Engagement | External engagement | Attribution service/read model | D5 | Owner/external/unknown fixtures |
| R5 Benchmarks | Competitor comparison | Cohort query/read model | D6 | Cohort sizes 4 and 5, matched window tests |
| R6 Priority | Portfolio ranking | Score config/service/read model | D7 | Weight validation, normalization, coverage and lifecycle unchanged tests |
