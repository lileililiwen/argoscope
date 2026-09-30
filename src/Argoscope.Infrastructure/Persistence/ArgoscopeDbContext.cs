using Argoscope.Domain.Alerts;
using Argoscope.Domain.Decisions;
using Argoscope.Domain.Engagement;
using Argoscope.Domain.Identity;
using Argoscope.Domain.Memberships;
using Argoscope.Domain.Packages;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Scores;
using Argoscope.Domain.Signals;
using Argoscope.Domain.Snapshots;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Argoscope.Infrastructure.Persistence;

public sealed class ArgoscopeDbContext : DbContext
{
    public ArgoscopeDbContext(DbContextOptions<ArgoscopeDbContext> options) : base(options) { }

    public DbSet<Portfolio> Portfolios => Set<Portfolio>();
    public DbSet<Repository> Repositories => Set<Repository>();
    public DbSet<PortfolioRepository> Memberships => Set<PortfolioRepository>();
    public DbSet<MetricSnapshot> MetricSnapshots => Set<MetricSnapshot>();
    public DbSet<CollectionCheckpoint> CollectionCheckpoints => Set<CollectionCheckpoint>();
    public DbSet<EngagementBucket> EngagementBuckets => Set<EngagementBucket>();
    public DbSet<ScoreConfiguration> ScoreConfigurations => Set<ScoreConfiguration>();
    public DbSet<PackageAssociation> PackageAssociations => Set<PackageAssociation>();
    public DbSet<PackageObservation> PackageObservations => Set<PackageObservation>();
    public DbSet<DecisionEntry> DecisionEntries => Set<DecisionEntry>();
    public DbSet<DecisionRevision> DecisionRevisions => Set<DecisionRevision>();
    public DbSet<DecisionEvidenceReference> DecisionEvidenceReferences => Set<DecisionEvidenceReference>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<AlertEvaluation> AlertEvaluations => Set<AlertEvaluation>();
    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();
    public DbSet<CommercialSignal> CommercialSignals => Set<CommercialSignal>();
    public DbSet<SignalReview> SignalReviews => Set<SignalReview>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PortfolioConfiguration());
        modelBuilder.ApplyConfiguration(new RepositoryConfiguration());
        modelBuilder.ApplyConfiguration(new MembershipConfiguration());
        modelBuilder.ApplyConfiguration(new MetricSnapshotConfiguration());
        modelBuilder.ApplyConfiguration(new CollectionCheckpointConfiguration());
        modelBuilder.ApplyConfiguration(new EngagementBucketConfiguration());
        modelBuilder.ApplyConfiguration(new ScoreConfigurationConfiguration());
        modelBuilder.ApplyConfiguration(new PackageAssociationConfiguration());
        modelBuilder.ApplyConfiguration(new PackageObservationConfiguration());
        modelBuilder.ApplyConfiguration(new DecisionEntryConfiguration());
        modelBuilder.ApplyConfiguration(new DecisionRevisionConfiguration());
        modelBuilder.ApplyConfiguration(new DecisionEvidenceReferenceConfiguration());
        modelBuilder.ApplyConfiguration(new AlertRuleConfiguration());
        modelBuilder.ApplyConfiguration(new AlertEvaluationConfiguration());
        modelBuilder.ApplyConfiguration(new DeliveryAttemptConfiguration());
        modelBuilder.ApplyConfiguration(new CommercialSignalConfiguration());
        modelBuilder.ApplyConfiguration(new SignalReviewConfiguration());
        modelBuilder.ApplyConfiguration(new TenantConfiguration());
        modelBuilder.ApplyConfiguration(new TenantMembershipConfiguration());
    }
}

public sealed class PortfolioConfiguration : IEntityTypeConfiguration<Portfolio>
{
    public void Configure(EntityTypeBuilder<Portfolio> b)
    {
        b.ToTable("portfolios");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).HasConversion(v => v.Value, v => Domain.Common.Id<Portfolio>.From(v));
        b.Property(p => p.Name).HasMaxLength(200).IsRequired();
        b.Property(p => p.TenantId).HasConversion(
            v => v.HasValue ? v.Value.Value : (Guid?)null,
            v => v.HasValue ? Domain.Common.Id<Domain.Identity.Tenant>.From(v.Value) : (Domain.Common.Id<Domain.Identity.Tenant>?)null);
        b.HasIndex(p => p.TenantId);
        b.Property(p => p.CreatedAtUtc).IsRequired();
        b.Property(p => p.UpdatedAtUtc).IsRequired();
    }
}

public sealed class RepositoryConfiguration : IEntityTypeConfiguration<Repository>
{
    public void Configure(EntityTypeBuilder<Repository> b)
    {
        b.ToTable("repositories");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).HasConversion(v => v.Value, v => Domain.Common.Id<Repository>.From(v));
        b.Property(r => r.NodeId).HasMaxLength(200).IsRequired();
        b.HasIndex(r => r.NodeId).IsUnique();
        b.HasIndex(r => new { r.OwnerLogin, r.Name }).IsUnique();
        b.Property(r => r.OwnerLogin).HasMaxLength(200).IsRequired();
        b.Property(r => r.Name).HasMaxLength(200).IsRequired();
        b.Property(r => r.Visibility).HasConversion<int>();
        b.Property(r => r.CreatedOnGithubAt);
        b.Property(r => r.PrimaryLanguage).HasMaxLength(100);
        b.Property(r => r.LastActivityAtUtc);
        b.Property(r => r.CreatedAtUtc).IsRequired();
        b.Property(r => r.UpdatedAtUtc).IsRequired();
    }
}

public sealed class MembershipConfiguration : IEntityTypeConfiguration<PortfolioRepository>
{
    public void Configure(EntityTypeBuilder<PortfolioRepository> b)
    {
        b.ToTable("portfolio_repositories");
        b.HasKey(m => m.Id);
        b.Property(m => m.Id).HasConversion(v => v.Value, v => Domain.Common.Id<PortfolioRepository>.From(v));
        b.Property(m => m.PortfolioId).HasConversion(v => v.Value, v => Domain.Common.Id<Portfolio>.From(v));
        b.Property(m => m.RepositoryId).HasConversion(v => v.Value, v => Domain.Common.Id<Repository>.From(v));
        b.Property(m => m.Role).HasConversion<int>();
        b.Property(m => m.Category).HasMaxLength(100);
        b.Property(m => m.Lifecycle).HasMaxLength(50).IsRequired();
        b.Property(m => m.CreatedAtUtc).IsRequired();
        b.Property(m => m.UpdatedAtUtc).IsRequired();
        b.HasIndex(m => new { m.PortfolioId, m.RepositoryId }).IsUnique();
    }
}

public sealed class MetricSnapshotConfiguration : IEntityTypeConfiguration<MetricSnapshot>
{
    public void Configure(EntityTypeBuilder<MetricSnapshot> b)
    {
        b.ToTable("metric_snapshots");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasConversion(v => v.Value, v => Domain.Common.Id<MetricSnapshot>.From(v));
        b.Property(s => s.RepositoryId).HasConversion(v => v.Value, v => Domain.Common.Id<Repository>.From(v));
        b.Property(s => s.MetricName).HasMaxLength(50).IsRequired();
        b.Property(s => s.MetricDate).IsRequired();
        b.Property(s => s.ProviderVersion).HasMaxLength(50).IsRequired();
        b.Property(s => s.Status).HasConversion<int>();
        b.Property(s => s.Value);
        b.Property(s => s.IsComplete).IsRequired();
        b.Property(s => s.ObservedAtUtc).IsRequired();
        b.Property(s => s.CollectedAtUtc).IsRequired();
        b.Property(s => s.DiagnosticCode).HasMaxLength(100);
        b.HasIndex(s => new { s.RepositoryId, s.MetricName, s.MetricDate, s.ProviderVersion }).IsUnique();
    }
}

public sealed class CollectionCheckpointConfiguration : IEntityTypeConfiguration<CollectionCheckpoint>
{
    public void Configure(EntityTypeBuilder<CollectionCheckpoint> b)
    {
        b.ToTable("collection_checkpoints");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).HasConversion(v => v.Value, v => Domain.Common.Id<CollectionCheckpoint>.From(v));
        b.Property(c => c.PortfolioId).HasConversion(v => v.Value, v => Domain.Common.Id<Portfolio>.From(v));
        b.Property(c => c.RepositoryId).HasConversion(v => v.Value, v => Domain.Common.Id<Repository>.From(v));
        b.Property(c => c.ProviderVersion).HasMaxLength(50).IsRequired();
        b.Property(c => c.Cursor).HasMaxLength(200);
        b.Property(c => c.LastStatus).HasConversion<int>();
        b.Property(c => c.LastAttemptAtUtc);
        b.Property(c => c.LastSuccessAtUtc);
        b.Property(c => c.RetryAfterUtc);
        b.Property(c => c.UpdatedAtUtc).IsRequired();
        b.HasIndex(c => new { c.PortfolioId, c.RepositoryId, c.ProviderVersion }).IsUnique();
    }
}

public sealed class EngagementBucketConfiguration : IEntityTypeConfiguration<EngagementBucket>
{
    public void Configure(EntityTypeBuilder<EngagementBucket> b)
    {
        b.ToTable("engagement_buckets");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasConversion(v => v.Value, v => Domain.Common.Id<EngagementBucket>.From(v));
        b.Property(e => e.RepositoryId).HasConversion(v => v.Value, v => Domain.Common.Id<Repository>.From(v));
        b.Property(e => e.BucketDate).IsRequired();
        b.Property(e => e.ExternalIssuesOpened).IsRequired();
        b.Property(e => e.ExternalPullRequestsOpened).IsRequired();
        b.Property(e => e.ExternalContributors).IsRequired();
        b.Property(e => e.OwnerIssuesOpened).IsRequired();
        b.Property(e => e.OwnerPullRequestsOpened).IsRequired();
        b.Property(e => e.OwnerContributors).IsRequired();
        b.Property(e => e.UnknownIssuesOpened).IsRequired();
        b.Property(e => e.UnknownPullRequestsOpened).IsRequired();
        b.Property(e => e.UnknownContributors).IsRequired();
        b.Property(e => e.ObservedAtUtc).IsRequired();
        b.Property(e => e.CollectedAtUtc).IsRequired();
        b.HasIndex(e => new { e.RepositoryId, e.BucketDate }).IsUnique();
    }
}

public sealed class ScoreConfigurationConfiguration : IEntityTypeConfiguration<ScoreConfiguration>
{
    public void Configure(EntityTypeBuilder<ScoreConfiguration> b)
    {
        b.ToTable("score_configurations");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasConversion(v => v.Value, v => Domain.Common.Id<ScoreConfiguration>.From(v));
        b.Property(s => s.PortfolioId).HasConversion(v => v.Value, v => Domain.Common.Id<Portfolio>.From(v));
        b.Property(s => s.Version).IsRequired();
        b.Property(s => s.CreatedAtUtc).IsRequired();
        b.Property(s => s.UpdatedAtUtc).IsRequired();
        b.OwnsMany(s => s.Factors, fb =>
        {
            fb.ToTable("score_configuration_factors");
            fb.WithOwner().HasForeignKey("ScoreConfigurationId");
            fb.HasKey("ScoreConfigurationId", "Name");
            fb.Property<string>("Name").HasMaxLength(50).IsRequired();
            fb.Property<double>("Weight").IsRequired();
            fb.Property<bool>("Enabled").IsRequired();
        });
        b.HasIndex(s => new { s.PortfolioId, s.Version }).IsUnique();
    }
}

public sealed class PackageAssociationConfiguration : IEntityTypeConfiguration<Domain.Packages.PackageAssociation>
{
    public void Configure(EntityTypeBuilder<Domain.Packages.PackageAssociation> b)
    {
        b.ToTable("package_associations");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).HasConversion(v => v.Value, v => Domain.Common.Id<Domain.Packages.PackageAssociation>.From(v));
        b.Property(p => p.RepositoryId).HasConversion(v => v.Value, v => Domain.Common.Id<Repository>.From(v));
        b.Property(p => p.Provider).HasConversion<int>();
        b.Property(p => p.Coordinate).HasMaxLength(200).IsRequired();
        b.Property(p => p.DefaultUnit).HasConversion<int>();
        b.Property(p => p.DefaultWindow).HasConversion<int>();
        b.Property(p => p.Status).HasConversion<int>();
        b.Property(p => p.AttentionReason).HasMaxLength(200);
        b.Property(p => p.CreatedAtUtc).IsRequired();
        b.Property(p => p.UpdatedAtUtc).IsRequired();
        b.HasIndex(p => new { p.RepositoryId, p.Provider, p.Coordinate }).IsUnique();
    }
}

public sealed class PackageObservationConfiguration : IEntityTypeConfiguration<Domain.Packages.PackageObservation>
{
    public void Configure(EntityTypeBuilder<Domain.Packages.PackageObservation> b)
    {
        b.ToTable("package_observations");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).HasConversion(v => v.Value, v => Domain.Common.Id<Domain.Packages.PackageObservation>.From(v));
        b.Property(p => p.PackageAssociationId).HasConversion(v => v.Value, v => Domain.Common.Id<Domain.Packages.PackageAssociation>.From(v));
        b.Property(p => p.Provider).HasConversion<int>();
        b.Property(p => p.ProviderVersion).HasMaxLength(50).IsRequired();
        b.Property(p => p.Unit).HasConversion<int>();
        b.Property(p => p.Window).HasConversion<int>();
        b.Property(p => p.WindowStartUtc).IsRequired();
        b.Property(p => p.WindowEndUtc).IsRequired();
        b.Property(p => p.Value).IsRequired();
        b.Property(p => p.Status).HasConversion<int>();
        b.Property(p => p.IsComplete).IsRequired();
        b.Property(p => p.ObservedAtUtc).IsRequired();
        b.Property(p => p.CollectedAtUtc).IsRequired();
        b.Property(p => p.DiagnosticCode).HasMaxLength(100);
        b.HasIndex(p => new { p.PackageAssociationId, p.Unit, p.Window, p.WindowStartUtc, p.WindowEndUtc }).IsUnique();
        b.HasIndex(p => p.PackageAssociationId);
    }
}

public sealed class DecisionEntryConfiguration : IEntityTypeConfiguration<Domain.Decisions.DecisionEntry>
{
    public void Configure(EntityTypeBuilder<Domain.Decisions.DecisionEntry> b)
    {
        b.ToTable("decision_entries");
        b.HasKey(d => d.Id);
        b.Property(d => d.Id).HasConversion(v => v.Value, v => Domain.Common.Id<Domain.Decisions.DecisionEntry>.From(v));
        b.Property(d => d.PortfolioId).HasConversion(v => v.Value, v => Domain.Common.Id<Domain.Portfolios.Portfolio>.From(v));
        b.Property(d => d.RepositoryId).HasConversion(v => v.HasValue ? v.Value.Value : (Guid?)null, v => v.HasValue ? Domain.Common.Id<Domain.Repositories.Repository>.From(v.Value) : (Domain.Common.Id<Domain.Repositories.Repository>?)null);
        b.Property(d => d.DecisionType).HasConversion<int>();
        b.Property(d => d.DecisionDate).IsRequired();
        b.Property(d => d.Rationale).HasMaxLength(DecisionEntry.MaxRationaleLength).IsRequired();
        b.Property(d => d.ReviewDate);
        b.Property(d => d.RevisionNumber).IsRequired();
        b.Property(d => d.IdempotencyKey).HasMaxLength(DecisionEntry.MaxIdempotencyKeyLength);
        b.Property(d => d.DeletedAtUtc);
        b.Property(d => d.CreatedAtUtc).IsRequired();
        b.Property(d => d.UpdatedAtUtc).IsRequired();
        b.HasIndex(d => new { d.PortfolioId, d.IdempotencyKey })
            .IsUnique()
            .HasFilter(null);
        b.HasIndex(d => d.PortfolioId);
        b.HasIndex(d => d.RepositoryId);
    }
}

public sealed class DecisionRevisionConfiguration : IEntityTypeConfiguration<Domain.Decisions.DecisionRevision>
{
    public void Configure(EntityTypeBuilder<Domain.Decisions.DecisionRevision> b)
    {
        b.ToTable("decision_revisions");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).HasConversion(v => v.Value, v => Domain.Common.Id<Domain.Decisions.DecisionRevision>.From(v));
        b.Property(r => r.DecisionEntryId).HasConversion(v => v.Value, v => Domain.Common.Id<Domain.Decisions.DecisionEntry>.From(v));
        b.Property(r => r.RevisionNumber).IsRequired();
        b.Property(r => r.Action).HasConversion<int>();
        b.Property(r => r.ActorId).HasMaxLength(100).IsRequired();
        b.Property(r => r.OccurredAtUtc).IsRequired();
        b.Property(r => r.BeforeJson).HasColumnType("text");
        b.Property(r => r.AfterJson).HasColumnType("text").IsRequired();
        b.Property(r => r.Note).HasMaxLength(2000);
        b.HasIndex(r => new { r.DecisionEntryId, r.RevisionNumber }).IsUnique();
        b.HasIndex(r => r.DecisionEntryId);
    }
}

public sealed class DecisionEvidenceReferenceConfiguration : IEntityTypeConfiguration<Domain.Decisions.DecisionEvidenceReference>
{
    public void Configure(EntityTypeBuilder<Domain.Decisions.DecisionEvidenceReference> b)
    {
        b.ToTable("decision_evidence_references");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasConversion(v => v.Value, v => Domain.Common.Id<Domain.Decisions.DecisionEvidenceReference>.From(v));
        b.Property(e => e.DecisionRevisionId).HasConversion(v => v.Value, v => Domain.Common.Id<Domain.Decisions.DecisionRevision>.From(v));
        b.Property(e => e.Kind).HasConversion<int>();
        b.Property(e => e.ReferenceId).IsRequired();
        b.Property(e => e.Label).HasMaxLength(DecisionEvidenceReference.MaxLabelLength);
        b.HasIndex(e => new { e.DecisionRevisionId, e.ReferenceId, e.Kind }).IsUnique();
        b.HasIndex(e => e.DecisionRevisionId);
    }
}

public sealed class AlertRuleConfiguration : IEntityTypeConfiguration<AlertRule>
{
    public void Configure(EntityTypeBuilder<AlertRule> b)
    {
        b.ToTable("alert_rules");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).HasConversion(v => v.Value, v => Domain.Common.Id<AlertRule>.From(v));
        b.Property(r => r.PortfolioId).HasConversion(v => v.Value, v => Domain.Common.Id<Domain.Portfolios.Portfolio>.From(v));
        b.Property(r => r.RepositoryId).HasConversion(v => v.HasValue ? v.Value.Value : (Guid?)null, v => v.HasValue ? Domain.Common.Id<Domain.Repositories.Repository>.From(v.Value) : (Domain.Common.Id<Domain.Repositories.Repository>?)null);
        b.Property(r => r.Name).HasMaxLength(AlertRule.MaxNameLength).IsRequired();
        b.Property(r => r.MetricKey).HasMaxLength(50).IsRequired();
        b.Property(r => r.Operator).HasConversion<int>();
        b.Property(r => r.Threshold).IsRequired();
        b.Property(r => r.MinimumCoverage).IsRequired();
        b.Property(r => r.CooldownHours).IsRequired();
        b.Property(r => r.Enabled).IsRequired();
        b.Property(r => r.Channel).HasConversion<int>();
        b.Property(r => r.Destination).HasMaxLength(AlertRule.MaxDestinationLength).IsRequired();
        b.Property(r => r.Secret).HasMaxLength(AlertRule.MaxSecretLength).IsRequired();
        b.Property(r => r.Version).IsRequired();
        b.Property(r => r.DeletedAtUtc);
        b.Property(r => r.CreatedAtUtc).IsRequired();
        b.Property(r => r.UpdatedAtUtc).IsRequired();
        b.HasIndex(r => r.PortfolioId);
    }
}

public sealed class AlertEvaluationConfiguration : IEntityTypeConfiguration<AlertEvaluation>
{
    public void Configure(EntityTypeBuilder<AlertEvaluation> b)
    {
        b.ToTable("alert_evaluations");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasConversion(v => v.Value, v => Domain.Common.Id<AlertEvaluation>.From(v));
        b.Property(e => e.RuleId).HasConversion(v => v.Value, v => Domain.Common.Id<AlertRule>.From(v));
        b.Property(e => e.PortfolioId).HasConversion(v => v.Value, v => Domain.Common.Id<Domain.Portfolios.Portfolio>.From(v));
        b.Property(e => e.RepositoryId).HasConversion(v => v.HasValue ? v.Value.Value : (Guid?)null, v => v.HasValue ? Domain.Common.Id<Domain.Repositories.Repository>.From(v.Value) : (Domain.Common.Id<Domain.Repositories.Repository>?)null);
        b.Property(e => e.MetricKey).HasMaxLength(50).IsRequired();
        b.Property(e => e.MetricWindowEndUtc).IsRequired();
        b.Property(e => e.MetricValue);
        b.Property(e => e.Coverage).IsRequired();
        b.Property(e => e.Status).HasConversion<int>();
        b.Property(e => e.Reason).HasMaxLength(200);
        b.Property(e => e.EvaluatedAtUtc).IsRequired();
        b.HasIndex(e => new { e.RuleId, e.MetricWindowEndUtc }).IsUnique();
        b.HasIndex(e => e.PortfolioId);
        b.HasIndex(e => e.RuleId);
    }
}

public sealed class DeliveryAttemptConfiguration : IEntityTypeConfiguration<DeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<DeliveryAttempt> b)
    {
        b.ToTable("delivery_attempts");
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).HasConversion(v => v.Value, v => Domain.Common.Id<DeliveryAttempt>.From(v));
        b.Property(a => a.EvaluationId).HasConversion(v => v.Value, v => Domain.Common.Id<AlertEvaluation>.From(v));
        b.Property(a => a.AttemptNumber).IsRequired();
        b.Property(a => a.State).HasConversion<int>();
        b.Property(a => a.ResponseCode);
        b.Property(a => a.Error).HasMaxLength(500);
        b.Property(a => a.NextRetryAtUtc);
        b.Property(a => a.CreatedAtUtc).IsRequired();
        b.Property(a => a.UpdatedAtUtc).IsRequired();
        b.HasIndex(a => new { a.EvaluationId, a.AttemptNumber }).IsUnique();
        b.HasIndex(a => a.EvaluationId);
    }
}

public sealed class CommercialSignalConfiguration : IEntityTypeConfiguration<CommercialSignal>
{
    public void Configure(EntityTypeBuilder<CommercialSignal> b)
    {
        b.ToTable("commercial_signals");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasConversion(v => v.Value, v => Domain.Common.Id<CommercialSignal>.From(v));
        b.Property(s => s.RepositoryId).HasConversion(v => v.Value, v => Domain.Common.Id<Domain.Repositories.Repository>.From(v));
        b.Property(s => s.SourceType).HasConversion<int>();
        b.Property(s => s.SourceNumber).IsRequired();
        b.Property(s => s.SourceUrl).HasMaxLength(500).IsRequired();
        b.Property(s => s.SourceUpdatedAtUtc).IsRequired();
        b.Property(s => s.ContentHash).HasMaxLength(64).IsRequired();
        b.Property(s => s.Excerpt).HasColumnType("text").IsRequired();
        b.Property(s => s.SourceAvailable).IsRequired();
        b.Property(s => s.SuggestionVersion).IsRequired();
        b.Property(s => s.Category).HasConversion<int>();
        b.Property(s => s.Confidence).IsRequired();
        b.Property(s => s.ClassifierVersion).HasMaxLength(50).IsRequired();
        b.Property(s => s.Rationale).HasMaxLength(CommercialSignal.MaxRationaleLength).IsRequired();
        b.Property(s => s.Status).HasConversion<int>();
        b.Property(s => s.CorrectedCategory).HasConversion<int>();
        b.Property(s => s.Reviewer).HasMaxLength(CommercialSignal.MaxReviewerLength);
        b.Property(s => s.ReviewedAtUtc);
        b.Property(s => s.Version).IsConcurrencyToken().IsRequired();
        b.Property(s => s.CreatedAtUtc).IsRequired();
        b.Property(s => s.UpdatedAtUtc).IsRequired();
        b.HasIndex(s => new { s.RepositoryId, s.SourceType, s.SourceNumber, s.ContentHash }).IsUnique();
        b.HasIndex(s => s.RepositoryId);
    }
}

public sealed class SignalReviewConfiguration : IEntityTypeConfiguration<SignalReview>
{
    public void Configure(EntityTypeBuilder<SignalReview> b)
    {
        b.ToTable("signal_reviews");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).HasConversion(v => v.Value, v => Domain.Common.Id<SignalReview>.From(v));
        b.Property(r => r.SignalId).HasConversion(v => v.Value, v => Domain.Common.Id<CommercialSignal>.From(v));
        b.Property(r => r.RevisionNumber).IsRequired();
        b.Property(r => r.Decision).HasConversion<int>();
        b.Property(r => r.PriorCategory).HasConversion<int>();
        b.Property(r => r.PriorStatus).HasConversion<int>();
        b.Property(r => r.CorrectedCategory).HasConversion<int>();
        b.Property(r => r.Reviewer).HasMaxLength(CommercialSignal.MaxReviewerLength).IsRequired();
        b.Property(r => r.OccurredAtUtc).IsRequired();
        b.Property(r => r.Note).HasMaxLength(SignalReview.MaxNoteLength);
        b.HasIndex(r => new { r.SignalId, r.RevisionNumber }).IsUnique();
        b.HasIndex(r => r.SignalId);
    }
}

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.ToTable("tenants");
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).HasConversion(v => v.Value, v => Domain.Common.Id<Tenant>.From(v));
        b.Property(t => t.Name).HasMaxLength(Tenant.MaxNameLength).IsRequired();
        b.Property(t => t.CreatedAtUtc).IsRequired();
        b.Property(t => t.UpdatedAtUtc).IsRequired();
    }
}

public sealed class TenantMembershipConfiguration : IEntityTypeConfiguration<TenantMembership>
{
    public void Configure(EntityTypeBuilder<TenantMembership> b)
    {
        b.ToTable("tenant_memberships");
        b.HasKey(m => m.Id);
        b.Property(m => m.Id).HasConversion(v => v.Value, v => Domain.Common.Id<TenantMembership>.From(v));
        b.Property(m => m.TenantId).HasConversion(v => v.Value, v => Domain.Common.Id<Tenant>.From(v));
        b.Property(m => m.Subject).HasMaxLength(TenantMembership.MaxSubjectLength).IsRequired();
        b.Property(m => m.DisplayName).HasMaxLength(TenantMembership.MaxDisplayNameLength).IsRequired();
        b.Property(m => m.Role).HasConversion<int>();
        b.Property(m => m.State).HasConversion<int>();
        b.Property(m => m.InviteToken).HasMaxLength(100);
        b.Property(m => m.InviteExpiresAtUtc);
        b.Property(m => m.CreatedAtUtc).IsRequired();
        b.Property(m => m.UpdatedAtUtc).IsRequired();
        b.HasIndex(m => m.TenantId);
        b.HasIndex(m => new { m.TenantId, m.Subject });
    }
}
