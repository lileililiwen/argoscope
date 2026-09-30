using Argoscope.Domain.Memberships;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Common;
using Xunit;

namespace Argoscope.UnitTests;

public class DomainEntityTests
{
    [Fact]
    public void Portfolio_rejects_blank_name()
    {
        Assert.Throws<DomainException>(() => new Portfolio(" ", default));
    }

    [Fact]
    public void Repository_locator_renamed_updates_display_only()
    {
        var repo = new Repository("node-1", "octo", "hello", RepositoryVisibility.Public, default);
        repo.UpdateLocator("octo-renamed", "hello-renamed", RepositoryVisibility.Public, default, null, null, null);
        Assert.Equal("octo-renamed", repo.OwnerLogin);
        Assert.Equal("hello-renamed", repo.Name);
        Assert.Equal("node-1", repo.NodeId);
    }

    [Fact]
    public void Membership_rejects_invalid_lifecycle()
    {
        var portfolioId = Id<Portfolio>.New();
        var repoId = Id<Repository>.New();
        Assert.Throws<DomainException>(() => new PortfolioRepository(portfolioId, repoId, MembershipRole.Owned, null, "NotALifecycle", default));
    }

    [Fact]
    public void Membership_change_audits_updated_at()
    {
        var portfolioId = Id<Portfolio>.New();
        var repoId = Id<Repository>.New();
        var t0 = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddDays(7);
        var m = new PortfolioRepository(portfolioId, repoId, MembershipRole.Owned, "ci", LifecycleStage.OpenSource, t0);
        m.Update("observability", LifecycleStage.Maintenance, t1);
        Assert.Equal("observability", m.Category);
        Assert.Equal(LifecycleStage.Maintenance, m.Lifecycle);
        Assert.Equal(t1, m.UpdatedAtUtc);
        Assert.Equal(t0, m.CreatedAtUtc);
    }

    [Fact]
    public void Repository_visibility_is_stored()
    {
        var repo = new Repository("node-1", "octo", "hello", RepositoryVisibility.Private, default);
        Assert.Equal(RepositoryVisibility.Private, repo.Visibility);
    }
}
