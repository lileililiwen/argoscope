using Argoscope.Domain.Engagement;
using Argoscope.Domain.Memberships;
using Argoscope.Domain.Packages;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Scores;
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
