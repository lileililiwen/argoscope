# package-adoption Specification

## Purpose
Expose package-level activity as timestamped, provider-attributed adoption signals.
## Requirements
### Requirement: Package association is owner-confirmed
The system MUST let an owner associate a repository with a validated package coordinate and provider, and MUST retain that identity separately from GitHub repository identity.

#### Scenario: Link a package
- **WHEN** an owner confirms a valid provider coordinate for a repository
- **THEN** Argoscope stores the association and makes it available for scheduled collection

#### Scenario: Invalid coordinate
- **WHEN** a coordinate does not satisfy provider validation
- **THEN** the API rejects it without creating an association

### Requirement: Observations retain source measurement semantics
Each observation MUST retain provider, value, unit, interval, observed timestamp and collection status; unlike units MUST NOT be combined.

#### Scenario: Display provider series
- **WHEN** valid observations exist
- **THEN** the API and dashboard show the provider-reported series with its unit, interval, freshness and coverage

#### Scenario: No observation versus zero
- **WHEN** the provider has not returned data or explicitly reports zero
- **THEN** Argoscope distinguishes unavailable data from a measured zero

### Requirement: Collection failure preserves last-good evidence
The system MUST make provider failures visible and retain previously collected observations.

#### Scenario: Provider is rate limited
- **WHEN** collection is rate limited or unavailable
- **THEN** the run records a retryable/stale status and preserves last-good observations

### Requirement: Adoption series expose source-attributed coverage
The read API and dashboard MUST surface each provider/unit/window series with its actual and expected point count, coverage, freshness and an explicit insufficient reason when the cohort is missing or stale. Unlike units MUST NOT be aggregated; missing data MUST be reported as unavailable and never as zero.

#### Scenario: Display the adoption series
- **WHEN** the dashboard loads the adoption view for a repository
- **THEN** each series is listed with its provider, unit, window, expected/actual points, coverage, last-observed timestamp, and an unavailable marker when no observations exist

#### Scenario: No series has data
- **WHEN** no observations have been collected yet
- **THEN** the API returns an insufficient-coverage reason and the dashboard shows an empty-state, not a zero count

