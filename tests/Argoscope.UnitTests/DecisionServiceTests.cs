using Argoscope.Application.Decisions;
using Argoscope.Domain.Common;
using Argoscope.Domain.Decisions;
using Argoscope.Domain.Memberships;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Snapshots;
using Xunit;

namespace Argoscope.UnitTests;

public class DecisionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private static (DecisionService svc,
        InMemoryDecisionEntryStore entryStore,
        InMemoryDecisionRevisionStore revisionStore,
        InMemoryDecisionEvidenceStore evidenceStore,
        InMemoryPortfolioRepository portfolios,
        InMemoryMembershipStore memberships,
        InMemoryMetricSnapshotStore snapshots,
        InMemoryRepositoryStore repositories,
        InMemoryPackageAssociationStore packageAssociations,
        InMemoryPackageObservationStore packageObservations,
        Portfolio portfolio,
        Repository repository)
        BuildHarness()
    {
        var entryStore = new InMemoryDecisionEntryStore();
        var revisionStore = new InMemoryDecisionRevisionStore();
        var evidenceStore = new InMemoryDecisionEvidenceStore();
        var portfolios = new InMemoryPortfolioRepository();
        var memberships = new InMemoryMembershipStore();
        var snapshots = new InMemoryMetricSnapshotStore();
        var repositories = new InMemoryRepositoryStore(memberships);
        var packageAssociations = new InMemoryPackageAssociationStore();
        var packageObservations = new InMemoryPackageObservationStore();
        var resolver = new DecisionEvidenceResolver(
            portfolios, memberships, snapshots, packageAssociations, packageObservations);
        var svc = new DecisionService(
            entryStore, revisionStore, evidenceStore, resolver, portfolios, memberships);

        var portfolio = new Portfolio("Demo", Now);
        portfolios.Add(portfolio);
        var repository = new Repository(
            "R_demo", "octo", "demo",
            RepositoryVisibility.Public, Now,
            createdOnGithubAt: new DateOnly(2024, 1, 1),
            primaryLanguage: "C#",
            lastActivityAtUtc: Now);
        repositories.Add(repository);
        memberships.Add(new PortfolioRepository(
            portfolio.Id, repository.Id, MembershipRole.Owned, "ci", LifecycleStage.OpenSource, Now));

        return (svc, entryStore, revisionStore, evidenceStore, portfolios, memberships, snapshots,
            repositories, packageAssociations, packageObservations, portfolio, repository);
    }

    [Fact]
    public async Task Create_persists_entry_and_initial_revision()
    {
        var (svc, entryStore, revisionStore, _, _, _, _, _, _, _, portfolio, _) = BuildHarness();

        var result = await svc.CreateAsync(new CreateDecisionCommand(
            portfolio.Id,
            RepositoryId: null,
            DecisionType: "Invest",
            DecisionDate: new DateOnly(2026, 9, 30),
            Rationale: "Adoption is climbing; worth doubling down.",
            ReviewDate: new DateOnly(2027, 3, 1),
            IdempotencyKey: null,
            Note: "initial entry",
            Evidence: Array.Empty<CreateDecisionEvidenceCommand>(),
            ActorId: DecisionActor.DefaultOwner,
            Now: Now), default);

        Assert.True(result.IsSuccess, result.Error?.Message);
        var dto = result.Value;
        Assert.Equal(1, dto.RevisionNumber);
        Assert.Equal("Invest", dto.DecisionType);
        Assert.Equal(new DateOnly(2026, 9, 30), dto.DecisionDate);
        Assert.Equal("owner", dto.LatestRevision.ActorId);
        Assert.Equal("Create", dto.LatestRevision.Action);

        var stored = await entryStore.FindAsync(Id<DecisionEntry>.From(dto.DecisionEntryId), default);
        Assert.NotNull(stored);
        Assert.Equal(1, stored!.RevisionNumber);
        Assert.Null(stored.DeletedAtUtc);

        var revisions = await revisionStore.ListByEntryAsync(stored.Id, default);
        var revision = Assert.Single(revisions);
        Assert.Equal(1, revision.RevisionNumber);
        Assert.Equal(DecisionRevisionAction.Create, revision.Action);
        Assert.Null(revision.BeforeJson);
        Assert.NotNull(revision.AfterJson);
    }

    [Fact]
    public async Task Update_increments_revision_and_preserves_history()
    {
        var (svc, entryStore, revisionStore, _, _, _, _, _, _, _, portfolio, _) = BuildHarness();

        var created = await svc.CreateAsync(new CreateDecisionCommand(
            portfolio.Id, null, "Continue", new DateOnly(2026, 9, 1),
            "Original rationale.", null, null, "create", Array.Empty<CreateDecisionEvidenceCommand>(),
            DecisionActor.DefaultOwner, Now), default);
        var entryId = Id<DecisionEntry>.From(created.Value.DecisionEntryId);

        var updated = await svc.UpdateAsync(new UpdateDecisionCommand(
            portfolio.Id, entryId, ExpectedRevision: 1, DecisionType: "Pause",
            DecisionDate: new DateOnly(2026, 9, 30), Rationale: "Reconsidered.",
            ReviewDate: null, Note: "second thought",
            Evidence: Array.Empty<CreateDecisionEvidenceCommand>(),
            ActorId: DecisionActor.DefaultOwner, Now: Now), default);

        Assert.True(updated.IsSuccess, updated.Error?.Message);
        Assert.Equal(2, updated.Value.RevisionNumber);
        Assert.Equal("Pause", updated.Value.DecisionType);
        Assert.Equal("Update", updated.Value.LatestRevision.Action);

        var revisions = await revisionStore.ListByEntryAsync(entryId, default);
        Assert.Equal(2, revisions.Count);
        Assert.Equal(1, revisions[0].RevisionNumber);
        Assert.Equal(2, revisions[1].RevisionNumber);
        Assert.NotNull(revisions[1].BeforeJson);

        var stored = await entryStore.FindAsync(entryId, default);
        Assert.Equal(2, stored!.RevisionNumber);
    }

    [Fact]
    public async Task Update_with_stale_expectedRevision_returns_conflict()
    {
        var (svc, _, _, _, _, _, _, _, _, _, portfolio, _) = BuildHarness();

        var created = await svc.CreateAsync(new CreateDecisionCommand(
            portfolio.Id, null, "Continue", new DateOnly(2026, 9, 1),
            "Original.", null, null, null, Array.Empty<CreateDecisionEvidenceCommand>(),
            DecisionActor.DefaultOwner, Now), default);
        var entryId = Id<DecisionEntry>.From(created.Value.DecisionEntryId);

        var ok = await svc.UpdateAsync(new UpdateDecisionCommand(
            portfolio.Id, entryId, 1, "Pause", new DateOnly(2026, 9, 30),
            "Bump.", null, null, Array.Empty<CreateDecisionEvidenceCommand>(),
            DecisionActor.DefaultOwner, Now), default);
        Assert.True(ok.IsSuccess);

        var stale = await svc.UpdateAsync(new UpdateDecisionCommand(
            portfolio.Id, entryId, 1, "Archive", new DateOnly(2026, 9, 30),
            "Stale write.", null, null, Array.Empty<CreateDecisionEvidenceCommand>(),
            DecisionActor.DefaultOwner, Now), default);
        Assert.True(stale.IsFailure);
        Assert.Equal("conflict", stale.Error!.Value.Code);
    }

    [Fact]
    public async Task Delete_then_Restore_round_trip()
    {
        var (svc, entryStore, revisionStore, _, _, _, _, _, _, _, portfolio, _) = BuildHarness();

        var created = await svc.CreateAsync(new CreateDecisionCommand(
            portfolio.Id, null, "Archive", new DateOnly(2026, 9, 1),
            "Sunset.", null, null, null, Array.Empty<CreateDecisionEvidenceCommand>(),
            DecisionActor.DefaultOwner, Now), default);
        var entryId = Id<DecisionEntry>.From(created.Value.DecisionEntryId);

        var deleted = await svc.DeleteAsync(new DeleteDecisionCommand(
            portfolio.Id, entryId, 1, "no longer active", DecisionActor.DefaultOwner, Now), default);
        Assert.True(deleted.IsSuccess);
        Assert.NotNull(deleted.Value.DeletedAtUtc);

        var liveList = await svc.ListByPortfolioAsync(portfolio.Id, includeDeleted: false, default);
        Assert.Empty(liveList);
        var trashList = await svc.ListByPortfolioAsync(portfolio.Id, includeDeleted: true, default);
        Assert.Single(trashList);

        var restored = await svc.RestoreAsync(new RestoreDecisionCommand(
            portfolio.Id, entryId, 2, "came back", DecisionActor.DefaultOwner, Now), default);
        Assert.True(restored.IsSuccess);
        Assert.Null(restored.Value.DeletedAtUtc);
        Assert.Equal(3, restored.Value.RevisionNumber);

        var revisions = await revisionStore.ListByEntryAsync(entryId, default);
        Assert.Equal(3, revisions.Count);
        Assert.Equal(DecisionRevisionAction.Delete, revisions[1].Action);
        Assert.Equal(DecisionRevisionAction.Restore, revisions[2].Action);
    }

    [Fact]
    public async Task Idempotency_key_returns_existing_entry()
    {
        var (svc, _, _, _, _, _, _, _, _, _, portfolio, _) = BuildHarness();

        var first = await svc.CreateAsync(new CreateDecisionCommand(
            portfolio.Id, null, "Continue", new DateOnly(2026, 9, 1),
            "Original.", null, "client-retry-1", null, Array.Empty<CreateDecisionEvidenceCommand>(),
            DecisionActor.DefaultOwner, Now), default);
        Assert.True(first.IsSuccess);

        var second = await svc.CreateAsync(new CreateDecisionCommand(
            portfolio.Id, null, "Continue", new DateOnly(2026, 9, 1),
            "Original retried with a different rationale.",
            null, "client-retry-1", null, Array.Empty<CreateDecisionEvidenceCommand>(),
            DecisionActor.DefaultOwner, Now), default);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.DecisionEntryId, second.Value.DecisionEntryId);
        Assert.Equal("Original.", second.Value.Rationale);
    }

    [Fact]
    public async Task Cross_portfolio_evidence_is_marked_unresolved()
    {
        var (svc, _, _, _, portfolios, memberships, snapshots, repositories, _, _, portfolio, _) = BuildHarness();

        // Create a foreign repository that is NOT a member of the
        // decision's portfolio and add a snapshot for it. The
        // resolver should refuse to link the reference.
        var foreignPortfolio = new Portfolio("Other", Now);
        portfolios.Add(foreignPortfolio);
        var foreignRepository = new Repository(
            "R_foreign", "octo", "foreign",
            RepositoryVisibility.Public, Now,
            createdOnGithubAt: null,
            primaryLanguage: "Go",
            lastActivityAtUtc: null);
        repositories.Add(foreignRepository);
        memberships.Add(new PortfolioRepository(
            foreignPortfolio.Id, foreignRepository.Id, MembershipRole.Owned,
            null, LifecycleStage.OpenSource, Now));
        var snapshot = new MetricSnapshot(
            foreignRepository.Id, "stars", new DateOnly(2026, 9, 30),
            "fake-1", 100, ProviderResultStatus.Available,
            isComplete: true, Now, Now);
        snapshots.Add(snapshot);

        var created = await svc.CreateAsync(new CreateDecisionCommand(
            portfolio.Id, null, "Continue", new DateOnly(2026, 9, 1),
            "Need this snapshot.", null, null, null,
            new[]
            {
                new CreateDecisionEvidenceCommand("Snapshot", snapshot.Id.Value, "linked"),
            },
            DecisionActor.DefaultOwner, Now), default);

        Assert.True(created.IsSuccess, created.Error?.Message);
        var evidence = Assert.Single(created.Value.Evidence);
        Assert.Equal("Unresolved", evidence.Resolution);
        Assert.Contains("cross-portfolio", evidence.SourceDestination);
    }

    [Fact]
    public async Task Missing_evidence_is_preserved_as_unresolved()
    {
        var (svc, _, _, _, _, _, _, _, _, _, portfolio, _) = BuildHarness();

        var missingId = Guid.NewGuid();
        var created = await svc.CreateAsync(new CreateDecisionCommand(
            portfolio.Id, null, "Continue", new DateOnly(2026, 9, 1),
            "Refs a deleted snapshot.", null, null, null,
            new[]
            {
                new CreateDecisionEvidenceCommand("Snapshot", missingId, "broken"),
            },
            DecisionActor.DefaultOwner, Now), default);

        Assert.True(created.IsSuccess, created.Error?.Message);
        var evidence = Assert.Single(created.Value.Evidence);
        Assert.Equal("Unresolved", evidence.Resolution);
        Assert.Equal(missingId, evidence.ReferenceId);
    }

    [Fact]
    public async Task Decision_never_changes_lifecycle_or_score()
    {
        var (svc, _, _, _, _, memberships, _, _, _, _, portfolio, repository) = BuildHarness();

        var created = await svc.CreateAsync(new CreateDecisionCommand(
            portfolio.Id, repository.Id, "Pause", new DateOnly(2026, 9, 1),
            "Pause the project.", null, null, null,
            Array.Empty<CreateDecisionEvidenceCommand>(),
            DecisionActor.DefaultOwner, Now), default);
        Assert.True(created.IsSuccess);

        var membership = (await memberships.ListByPortfolioAsync(portfolio.Id, default))
            .Single(m => m.RepositoryId == repository.Id);
        Assert.Equal(LifecycleStage.OpenSource, membership.Lifecycle);
    }

    [Fact]
    public async Task Create_with_repository_not_in_portfolio_is_rejected()
    {
        var (svc, _, _, _, portfolios, memberships, _, _, _, _, portfolio, _) = BuildHarness();

        // Create a fresh repository with no membership for the portfolio.
        var orphan = new Repository(
            "R_orphan", "octo", "orphan",
            RepositoryVisibility.Public, Now,
            createdOnGithubAt: null,
            primaryLanguage: "Go",
            lastActivityAtUtc: null);

        var createAttempt = await svc.CreateAsync(new CreateDecisionCommand(
            portfolio.Id, orphan.Id, "Continue", new DateOnly(2026, 9, 1),
            "Tried to attach to an orphan.", null, null, null,
            Array.Empty<CreateDecisionEvidenceCommand>(),
            DecisionActor.DefaultOwner, Now), default);

        Assert.True(createAttempt.IsFailure);
        Assert.Equal("validation", createAttempt.Error!.Value.Code);
    }

    [Fact]
    public async Task Review_date_before_decision_date_is_rejected()
    {
        var (svc, _, _, _, _, _, _, _, _, _, portfolio, _) = BuildHarness();

        var result = await svc.CreateAsync(new CreateDecisionCommand(
            portfolio.Id, null, "Revisit", new DateOnly(2026, 9, 30),
            "Future revisit.", new DateOnly(2026, 9, 1), null, null,
            Array.Empty<CreateDecisionEvidenceCommand>(),
            DecisionActor.DefaultOwner, Now), default);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error!.Value.Code);
    }
}
