# commercial-signals Specification

## Purpose
TBD - created by archiving change commercial-signal-review. Update Purpose after archive.
## Requirements
### Requirement: Eligible issue/PR text is collected into bounded projections
The system MUST collect only issue/PR title and body for eligible sources and MUST store a redacted excerpt capped at 4,000 characters with source version identity.

#### Scenario: Collect eligible issue text
- **WHEN** the owner collects commercial signals for a repository with eligible issues/PRs
- **THEN** one queued record per source stores repository ID, issue/PR number, URL, source updated-at, content hash and the redacted excerpt

#### Scenario: Source contains secrets or oversized body
- **WHEN** the title/body contains email addresses, tokens, or more than 4,000 characters
- **THEN** the stored excerpt is redacted and truncated and no author email, token or full thread is stored

#### Scenario: Source has no usable text
- **WHEN** both title and body are empty
- **THEN** no suggestion is created

### Requirement: Classifier suggestions are typed, validated and retryable
The system MUST validate classifier output against the category vocabulary and confidence range, MUST default low-confidence output to Unclear, and MUST record provider/model failure as a retryable unclassified state.

#### Scenario: Classify across categories
- **WHEN** the classifier returns a known category with confidence in [0,1]
- **THEN** the suggestion stores the category, confidence, schema version and a short rationale referencing the excerpt

#### Scenario: Malformed or failed classification
- **WHEN** the classifier returns malformed output or the provider fails
- **THEN** the suggestion is recorded as retryable Unclassified and no review state is set

#### Scenario: Low confidence output
- **WHEN** the classifier returns confidence below 0.65
- **THEN** the stored category defaults to Unclear

### Requirement: Suggestions are versioned and reviews are append-only
The system MUST create a new suggestion version when source content changes, MUST NOT overwrite a human decision on reclassification, and MUST append an immutable review audit event per transition with optimistic concurrency.

#### Scenario: Duplicate job for unchanged content
- **WHEN** a collection job repeats for the same source version
- **THEN** it returns the existing suggestion without creating a duplicate

#### Scenario: Source content changes after review
- **WHEN** the source title/body hash changes after a human decision
- **THEN** a new pending suggestion version is created and the prior decision and audit remain unchanged

#### Scenario: Owner accepts a suggestion
- **WHEN** the owner reviews a pending suggestion with the current version
- **THEN** the status transitions Pending to Accepted, Rejected or Corrected with reviewer, time and prior values recorded immutably

#### Scenario: Concurrent reviewers conflict
- **WHEN** a review supplies a stale expected version
- **THEN** the API returns conflict and leaves the suggestion and history unchanged

### Requirement: Review API and queue UI keep scores and GitHub unchanged
The system MUST expose scoped read/review endpoints and a review queue UI, MUST retain review audit when the source is deleted, and MUST never alter scores, lifecycle state, labels or GitHub resources.

#### Scenario: List review queue
- **WHEN** the owner lists commercial signals for a repository filtered by state
- **THEN** each row exposes the source reference, category, confidence, status, suggestion version and review history

#### Scenario: Source deleted after suggestion
- **WHEN** the source issue/PR is deleted upstream
- **THEN** the suggestion is marked source-unavailable while the review audit remains readable

#### Scenario: Classifier runs over the portfolio
- **WHEN** classification completes for any source
- **THEN** no portfolio rank, score, lifecycle, label or GitHub resource is modified

