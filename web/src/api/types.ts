// Shared API types. These mirror the DTOs returned by the v1 API.

export type RepositoryVisibility = 'Public' | 'Private' | 'Internal' | 'Unknown';
export type MembershipRole = 'Owned' | 'Competitor';
export type ProviderResultStatus =
  | 'Available'
  | 'Partial'
  | 'Stale'
  | 'RateLimited'
  | 'Unauthorized'
  | 'Forbidden'
  | 'NotFound'
  | 'Unavailable'
  | 'Malformed';

export interface PortfolioDto {
  id: string;
  name: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface MembershipDto {
  membershipId: string;
  repositoryId: string;
  nodeId: string;
  ownerLogin: string;
  name: string;
  fullName: string;
  visibility: RepositoryVisibility;
  role: MembershipRole;
  category: string | null;
  lifecycle: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface VelocityResult {
  metricName: string;
  startDate: string | null;
  endDate: string | null;
  startValue: number | null;
  endValue: number | null;
  absoluteChange: number | null;
  percentChange: number | null;
  perDay: number | null;
  elapsedDays: number;
  coveredDays: number;
  isNewSignal: boolean;
  insufficientReason: string | null;
}

export interface AccelerationResult {
  metricName: string;
  current30DayChange: number | null;
  previous30DayChange: number | null;
  acceleration: number | null;
  elapsedDays: number;
  coveredDays: number;
  insufficientReason: string | null;
}

export interface EngagementSummary {
  windowStart: string;
  windowEnd: string;
  externalIssues: number;
  externalPullRequests: number;
  externalContributors: number;
  ownerIssues: number;
  ownerPullRequests: number;
  ownerContributors: number;
  unknownIssues: number;
  unknownPullRequests: number;
  unknownContributors: number;
  asOfUtc: string;
  coveredDays: number;
  insufficientReason: string | null;
}

export interface OverviewRow {
  membershipId: string;
  repositoryId: string;
  nodeId: string;
  ownerLogin: string;
  name: string;
  role: MembershipRole;
  category: string | null;
  lifecycle: string;
  velocities: Record<string, VelocityResult>;
  acceleration: AccelerationResult | null;
  engagement: EngagementSummary | null;
  lastSnapshotAtUtc: string | null;
  latestStatus: ProviderResultStatus;
  diagnosticCode: string | null;
}

export interface PortfolioOverview {
  portfolioId: string;
  window: string;
  windowStart: string;
  windowEnd: string;
  asOfUtc: string;
  rows: OverviewRow[];
}

export interface PeerBenchmarkResult {
  category: string;
  metricName: string;
  windowDays: number;
  cohortSize: number;
  eligibleSize: number;
  median: number | null;
  asOfUtc: string;
  insufficientReason: string | null;
}

export interface BenchmarkReport {
  portfolioId: string;
  window: string;
  asOfUtc: string;
  results: PeerBenchmarkResult[];
}

export interface ScoreFactor {
  name: string;
  weight: number;
  enabled: boolean;
}

export interface ScoreConfigurationDto {
  version: number;
  factors: ScoreFactor[];
  isDefault: boolean;
}

export interface RepositoryMetrics {
  repositoryId: string;
  nodeId: string;
  ownerLogin: string;
  name: string;
  visibility: RepositoryVisibility;
  createdOnGithubAt: string | null;
  primaryLanguage: string | null;
  lastSnapshotAtUtc: string | null;
  latestStatus: ProviderResultStatus;
  diagnosticCode: string | null;
  velocities: Record<string, VelocityResult>;
  acceleration: AccelerationResult | null;
  engagement: EngagementSummary | null;
}

export interface CollectionRunResult {
  repositoryId: string;
  snapshotsWritten: number;
  engagementBucketsWritten: number;
  repositoryStatus: ProviderResultStatus;
  metricsStatus: ProviderResultStatus;
  engagementStatus: ProviderResultStatus;
  runAtUtc: string;
}

export type PackageProvider =
  | 'DockerHub'
  | 'Npm'
  | 'NuGet'
  | 'PyPI'
  | 'CratesIo';

export type PackageUnit = 'Pulls' | 'Downloads';
export type PackageWindow = 'Daily' | 'Weekly' | 'Monthly' | 'Cumulative';
export type PackageAssociationStatus = 'Linked' | 'AttentionRequired' | 'Removed';

export interface PackageAssociationDto {
  associationId: string;
  repositoryId: string;
  provider: PackageProvider;
  coordinate: string;
  defaultUnit: PackageUnit;
  defaultWindow: PackageWindow;
  status: PackageAssociationStatus;
  attentionReason: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface PackageCollectionRunResult {
  associationId: string;
  observationsWritten: number;
  observationsPreserved: number;
  metadataStatus: ProviderResultStatus;
  pageStatus: ProviderResultStatus;
  diagnosticCode: string | null;
  runAtUtc: string;
}

export interface PackageAdoptionPoint {
  windowStartUtc: string;
  windowEndUtc: string;
  observedAtUtc: string;
  value: number;
  status: ProviderResultStatus;
  isComplete: boolean;
  diagnosticCode: string | null;
}

export interface PackageAdoptionSeries {
  associationId: string;
  provider: PackageProvider;
  coordinate: string;
  unit: PackageUnit;
  window: PackageWindow;
  firstObservedAtUtc: string;
  lastObservedAtUtc: string;
  expectedPoints: number;
  actualPoints: number;
  coverage: number;
  status: string;
  points: PackageAdoptionPoint[];
}

export interface PackageAdoptionReport {
  repositoryId: string;
  asOfUtc: string;
  series: PackageAdoptionSeries[];
  seriesWithData: number;
  seriesStale: number;
  seriesMissing: number;
  insufficientReason: string | null;
}

export type DecisionType = 'Continue' | 'Invest' | 'Pause' | 'Archive' | 'Revisit';
export type DecisionRevisionAction = 'Create' | 'Update' | 'Delete' | 'Restore';
export type DecisionEvidenceKind = 'Snapshot' | 'PackageObservation' | 'CommercialSignal';
export type DecisionEvidenceResolution = 'Resolved' | 'Unresolved';

export interface DecisionEvidenceDto {
  evidenceId: string;
  kind: DecisionEvidenceKind;
  referenceId: string;
  resolution: DecisionEvidenceResolution;
  sourceDestination: string;
  label: string | null;
  evidenceDate: string | null;
}

export interface DecisionRevisionSummaryDto {
  revisionNumber: number;
  action: DecisionRevisionAction;
  actorId: string;
  occurredAtUtc: string;
  note: string | null;
}

export interface DecisionEntryDto {
  decisionEntryId: string;
  portfolioId: string;
  repositoryId: string | null;
  decisionType: DecisionType;
  decisionDate: string;
  rationale: string;
  reviewDate: string | null;
  revisionNumber: number;
  idempotencyKey: string | null;
  deletedAtUtc: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  evidence: DecisionEvidenceDto[];
  latestRevision: DecisionRevisionSummaryDto;
}

export interface DecisionRevisionDto {
  revisionNumber: number;
  action: DecisionRevisionAction;
  actorId: string;
  occurredAtUtc: string;
  note: string | null;
  evidence: DecisionEvidenceDto[];
}

export type AlertOperator = 'GreaterThan' | 'GreaterThanOrEqual' | 'LessThan' | 'LessThanOrEqual';
export type AlertChannel = 'Email' | 'Webhook';
export type AlertMetricKey =
  | 'stars_7d'
  | 'stars_30d'
  | 'external_engagement_30d'
  | 'momentum_score'
  | 'snapshot_staleness_hours';

export interface AlertRuleDto {
  ruleId: string;
  portfolioId: string;
  repositoryId: string | null;
  name: string;
  metricKey: AlertMetricKey;
  operator: AlertOperator;
  threshold: number;
  minimumCoverage: number;
  cooldownHours: number;
  enabled: boolean;
  channel: AlertChannel;
  destinationMasked: string;
  hasSecret: boolean;
  version: number;
  deletedAtUtc: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface DeliveryAttemptDto {
  attemptId: string;
  attemptNumber: number;
  state: string;
  responseCode: number | null;
  error: string | null;
  nextRetryAtUtc: string | null;
  createdAtUtc: string;
}

export interface AlertEvaluationDto {
  evaluationId: string;
  ruleId: string;
  ruleName: string;
  portfolioId: string;
  repositoryId: string | null;
  metricKey: AlertMetricKey;
  metricWindowEndUtc: string;
  metricValue: number | null;
  coverage: number;
  status: string;
  reason: string | null;
  evaluatedAtUtc: string;
  attempts: DeliveryAttemptDto[];
}
