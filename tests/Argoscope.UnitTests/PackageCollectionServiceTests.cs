using Argoscope.Application.Packages;
using Argoscope.Domain.Common;
using Argoscope.Domain.Packages;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Snapshots;
using Argoscope.Packages;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Argoscope.UnitTests;

public class PackageCollectionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Writes_idempotent_observations_for_each_observation_fixture()
    {
        var fake = new FakePackageMetricsProvider(PackageProvider.DockerHub, "fake-dockerhub-1");
        var day0 = Now.AddDays(-3);
        fake.AddExistence("argoscope/sample-runner", exists: true);
        fake.AddObservation(new FakePackageMetricsProvider.ObservationFixture(
            "argoscope/sample-runner", PackageUnit.Pulls, PackageWindow.Cumulative,
            day0, day0.AddDays(1), 100, day0));
        fake.AddObservation(new FakePackageMetricsProvider.ObservationFixture(
            "argoscope/sample-runner", PackageUnit.Pulls, PackageWindow.Cumulative,
            Now.AddDays(-1), Now, 120, Now.AddDays(-1)));

        var assoc = new PackageAssociation(Id<Repository>.New(), PackageProvider.DockerHub,
            "argoscope/sample-runner", PackageUnit.Pulls, PackageWindow.Cumulative, Now);
        var registry = new PackageProviderRegistry(new IPackageMetricsProvider[] { fake });
        var (svc, assocStore, obsStore) = BuildService(registry, assoc);

        var result = await svc.RunAsync(new PackageCollectionRequest(
            assoc.RepositoryId, assoc.Id, PackageProvider.DockerHub, assoc.Coordinate,
            assoc.DefaultUnit, assoc.DefaultWindow, Now), default);

        Assert.Equal(ProviderResultStatus.Available, result.MetadataStatus);
        Assert.Equal(ProviderResultStatus.Available, result.PageStatus);
        Assert.Equal(2, result.ObservationsWritten);
        Assert.Equal(0, result.ObservationsPreserved);
        Assert.Equal(2, (await obsStore.ListByAssociationAsync(assoc.Id, default)).Count);
    }

    [Fact]
    public async Task Not_found_transitions_association_to_attention_required_and_writes_nothing()
    {
        var fake = new FakePackageMetricsProvider(PackageProvider.DockerHub);
        // No existence entry → probe returns NotFound.
        fake.AddObservation(new FakePackageMetricsProvider.ObservationFixture(
            "argoscope/sample-runner", PackageUnit.Pulls, PackageWindow.Cumulative,
            Now.AddDays(-1), Now, 100, Now.AddDays(-1)));

        var assoc = new PackageAssociation(Id<Repository>.New(), PackageProvider.DockerHub,
            "argoscope/sample-runner", PackageUnit.Pulls, PackageWindow.Cumulative, Now);
        var registry = new PackageProviderRegistry(new IPackageMetricsProvider[] { fake });
        var (svc, assocStore, obsStore) = BuildService(registry, assoc);

        var result = await svc.RunAsync(new PackageCollectionRequest(
            assoc.RepositoryId, assoc.Id, PackageProvider.DockerHub, assoc.Coordinate,
            assoc.DefaultUnit, assoc.DefaultWindow, Now), default);

        Assert.Equal(ProviderResultStatus.NotFound, result.MetadataStatus);
        Assert.Equal(0, result.ObservationsWritten);
        var reloaded = await assocStore.FindAsync(assoc.Id, default);
        Assert.NotNull(reloaded);
        Assert.Equal(PackageAssociationStatus.AttentionRequired, reloaded!.Status);
        Assert.Equal("package-not-found", reloaded.AttentionReason);
    }

    [Fact]
    public async Task Idempotent_upserts_preserve_existing_complete_observations()
    {
        var fake = new FakePackageMetricsProvider(PackageProvider.Npm, "fake-npm-1");
        fake.AddExistence("@argoscope/sample", exists: true);
        var window = Now.AddDays(-2);
        fake.AddObservation(new FakePackageMetricsProvider.ObservationFixture(
            "@argoscope/sample", PackageUnit.Downloads, PackageWindow.Weekly,
            window, Now, 240, window));

        var assoc = new PackageAssociation(Id<Repository>.New(), PackageProvider.Npm,
            "@argoscope/sample", PackageUnit.Downloads, PackageWindow.Weekly, Now);
        var registry = new PackageProviderRegistry(new IPackageMetricsProvider[] { fake });
        var (svc, assocStore, obsStore) = BuildService(registry, assoc);

        // First run.
        var r1 = await svc.RunAsync(new PackageCollectionRequest(
            assoc.RepositoryId, assoc.Id, PackageProvider.Npm, assoc.Coordinate,
            assoc.DefaultUnit, assoc.DefaultWindow, Now), default);
        Assert.Equal(1, r1.ObservationsWritten);

        // Second run, same observation: collection service should
        // detect the prior complete row and preserve it.
        var r2 = await svc.RunAsync(new PackageCollectionRequest(
            assoc.RepositoryId, assoc.Id, PackageProvider.Npm, assoc.Coordinate,
            assoc.DefaultUnit, assoc.DefaultWindow, Now), default);
        Assert.Equal(0, r2.ObservationsWritten);
        Assert.Equal(1, r2.ObservationsPreserved);
        Assert.Single((await obsStore.ListByAssociationAsync(assoc.Id, default)));
    }

    [Fact]
    public async Task Transient_provider_failure_does_not_modify_association_or_observations()
    {
        var fake = new FakePackageMetricsProvider(PackageProvider.CratesIo);
        fake.MetadataStatuses["rival-monitor"] = ProviderResultStatus.RateLimited;
        var assoc = new PackageAssociation(Id<Repository>.New(), PackageProvider.CratesIo,
            "rival-monitor", PackageUnit.Downloads, PackageWindow.Cumulative, Now);
        var registry = new PackageProviderRegistry(new IPackageMetricsProvider[] { fake });
        var (svc, assocStore, obsStore) = BuildService(registry, assoc);

        var result = await svc.RunAsync(new PackageCollectionRequest(
            assoc.RepositoryId, assoc.Id, PackageProvider.CratesIo, assoc.Coordinate,
            assoc.DefaultUnit, assoc.DefaultWindow, Now), default);

        Assert.Equal(ProviderResultStatus.RateLimited, result.MetadataStatus);
        Assert.Equal(0, result.ObservationsWritten);
        var reloaded = await assocStore.FindAsync(assoc.Id, default);
        Assert.NotNull(reloaded);
        Assert.Equal(PackageAssociationStatus.Linked, reloaded!.Status);
    }

    private static (PackageCollectionService svc, InMemoryPackageAssociationStore assocStore, InMemoryPackageObservationStore obsStore)
        BuildService(PackageProviderRegistry registry, params PackageAssociation[] seed)
    {
        var assocStore = new InMemoryPackageAssociationStore();
        var obsStore = new InMemoryPackageObservationStore();
        foreach (var a in seed) assocStore.AddAsync(a, default).GetAwaiter().GetResult();
        var svc = new PackageCollectionService(registry, assocStore, obsStore, NullLogger<PackageCollectionService>.Instance, new FixedClock(Now));
        return (svc, assocStore, obsStore);
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;
        public DateTimeOffset UtcNow { get; }
    }
}
