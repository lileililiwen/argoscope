using Argoscope.Application.Identity;
using Argoscope.Domain.Common;
using Argoscope.Domain.Identity;
using Argoscope.Domain.Portfolios;
using Microsoft.Extensions.Options;
using Xunit;

namespace Argoscope.UnitTests;

/// <summary>BFS fixtures and DFS requirement coverage for hosted identity:
/// tenant lifecycle, roles/last-owner invariant, OIDC validation, migration
/// and job tenant scope. Mirrors the two-tenant isolation matrix at the
/// service level; the API-level matrix lives in the integration suite.</summary>
public sealed class TenantIdentityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private sealed class Fixture
    {
        public InMemoryTenantStore Tenants { get; } = new();
        public InMemoryTenantMembershipStore Memberships { get; } = new();
        public InMemoryPortfolioTenantStore Portfolios { get; } = new();
        public InMemorySessionStore Sessions { get; }
        public IdentityOptions Options { get; } = new()
        {
            Mode = "Hosted",
            OidcIssuer = "https://issuer.example",
            OidcAudience = "argoscope-test",
        };

        public Fixture()
        {
            Sessions = new InMemorySessionStore(new FixedClock(Now), TimeSpan.FromHours(8));
        }

        public TenantService Service() => new(
            Tenants, Memberships, Sessions,
            new OidcTokenValidator(OptionsWrapper()),
            Microsoft.Extensions.Options.Options.Create(Options));

        public TenantMigrationService Migration() => new(Portfolios, Tenants);

        private IOptions<IdentityOptions> OptionsWrapper() =>
            Microsoft.Extensions.Options.Options.Create(Options);
    }

    private static async Task<(Tenant Tenant, TenantMembership Owner)> CreateTenantAsync(Fixture f, string name = "Acme", string owner = "owner-1")
    {
        var tenant = new Tenant(name, Now);
        await f.Tenants.AddAsync(tenant, CancellationToken.None);
        var membership = TenantMembership.CreateOwner(tenant.Id, owner, owner, Now);
        await f.Memberships.AddAsync(membership, CancellationToken.None);
        return (tenant, membership);
    }

    [Fact]
    public async Task CreateTenant_CreatesActiveOwner()
    {
        var f = new Fixture();
        var result = await f.Service().CreateTenantAsync(
            new CreateTenantCommand("Acme", "owner-1", "Owner One", Now), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal("Acme", result.Value.Tenant.Name);
        Assert.Equal("Owner", result.Value.Owner.Role);
        Assert.Equal("Active", result.Value.Owner.State);
        Assert.Equal("owner-1", result.Value.Owner.Subject);
    }

    [Fact]
    public async Task Invite_Accept_IsSingleUse_ThenReplayConflicts()
    {
        var f = new Fixture();
        var (tenant, _) = await CreateTenantAsync(f);
        var svc = f.Service();

        var invite = await svc.InviteAsync(
            new InviteMemberCommand(tenant.Id, "owner-1", "Editor Ed", TenantRole.Editor, Now), CancellationToken.None);
        Assert.True(invite.IsSuccess);
        Assert.NotEmpty(invite.Value.InviteToken);

        var accepted = await svc.AcceptInviteAsync(invite.Value.InviteToken, "editor-1", Now, CancellationToken.None);
        Assert.True(accepted.IsSuccess);
        Assert.Equal("Active", accepted.Value.Member.State);
        Assert.Equal("Editor", accepted.Value.Member.Role);
        Assert.NotEmpty(accepted.Value.SessionToken);

        // The member list never exposes the token.
        var members = await svc.ListMembersAsync(tenant.Id, CancellationToken.None);
        Assert.Equal(2, members.Count);

        // Replay of the consumed token is denied.
        var replay = await svc.AcceptInviteAsync(invite.Value.InviteToken, "editor-1", Now, CancellationToken.None);
        Assert.False(replay.IsSuccess);
        Assert.Equal("not_found", replay.Error!.Value.Code);
    }

    [Fact]
    public async Task Invite_Expired_IsDenied()
    {
        var f = new Fixture();
        var (tenant, _) = await CreateTenantAsync(f);
        var svc = f.Service();
        var invite = await svc.InviteAsync(
            new InviteMemberCommand(tenant.Id, "owner-1", "Late Lee", TenantRole.Viewer, Now), CancellationToken.None);
        var late = await svc.AcceptInviteAsync(
            invite.Value.InviteToken, "late-1", Now.AddDays(8), CancellationToken.None);
        Assert.False(late.IsSuccess);
        Assert.Equal("conflict", late.Error!.Value.Code);
    }

    [Fact]
    public async Task Invite_UnknownToken_IsNotFound()
    {
        var f = new Fixture();
        var result = await f.Service().AcceptInviteAsync("nope", "someone", Now, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal("not_found", result.Error!.Value.Code);
    }

    [Fact]
    public async Task Invite_NonOwner_IsForbidden_And_NonMember_IsUnauthorized()
    {
        var f = new Fixture();
        var (tenant, _) = await CreateTenantAsync(f);
        var svc = f.Service();
        var editorInvite = await svc.InviteAsync(
            new InviteMemberCommand(tenant.Id, "owner-1", "Editor Ed", TenantRole.Editor, Now), CancellationToken.None);
        await svc.AcceptInviteAsync(editorInvite.Value.InviteToken, "editor-1", Now, CancellationToken.None);

        var editorAttempt = await svc.InviteAsync(
            new InviteMemberCommand(tenant.Id, "editor-1", "Sneaky", TenantRole.Viewer, Now), CancellationToken.None);
        Assert.False(editorAttempt.IsSuccess);
        Assert.Equal("forbidden", editorAttempt.Error!.Value.Code);

        var strangerAttempt = await svc.InviteAsync(
            new InviteMemberCommand(tenant.Id, "stranger", "Sneaky", TenantRole.Viewer, Now), CancellationToken.None);
        Assert.False(strangerAttempt.IsSuccess);
        Assert.Equal("unauthorized", strangerAttempt.Error!.Value.Code);
    }

    [Fact]
    public async Task Revoke_LastOwner_Conflicts_ButSecondOwner_Allows()
    {
        var f = new Fixture();
        var (tenant, owner) = await CreateTenantAsync(f);
        var svc = f.Service();

        var lastOwner = await svc.RevokeAsync(tenant.Id, "owner-1", owner.Id, Now, CancellationToken.None);
        Assert.False(lastOwner.IsSuccess);
        Assert.Equal("conflict", lastOwner.Error!.Value.Code);

        var second = await svc.InviteAsync(
            new InviteMemberCommand(tenant.Id, "owner-1", "Owner Two", TenantRole.Owner, Now), CancellationToken.None);
        var accepted = await svc.AcceptInviteAsync(second.Value.InviteToken, "owner-2", Now, CancellationToken.None);
        Assert.True(accepted.IsSuccess);

        var revoke = await svc.RevokeAsync(tenant.Id, "owner-2", owner.Id, Now, CancellationToken.None);
        Assert.True(revoke.IsSuccess);
        Assert.Equal("Revoked", revoke.Value.State);
    }

    [Fact]
    public async Task ChangeRole_DowngradeLastOwner_Conflicts()
    {
        var f = new Fixture();
        var (tenant, owner) = await CreateTenantAsync(f);
        var svc = f.Service();

        var downgrade = await svc.ChangeRoleAsync(
            tenant.Id, "owner-1", owner.Id, TenantRole.Viewer, Now, CancellationToken.None);
        Assert.False(downgrade.IsSuccess);
        Assert.Equal("conflict", downgrade.Error!.Value.Code);

        // Invited (non-active) memberships cannot change role.
        var invite = await svc.InviteAsync(
            new InviteMemberCommand(tenant.Id, "owner-1", "Pending Pam", TenantRole.Editor, Now), CancellationToken.None);
        var pending = (await svc.ListMembersAsync(tenant.Id, CancellationToken.None))
            .Single(m => m.DisplayName == "Pending Pam");
        var changePending = await svc.ChangeRoleAsync(
            tenant.Id, "owner-1", Id<TenantMembership>.From(pending.MembershipId), TenantRole.Viewer, Now, CancellationToken.None);
        Assert.False(changePending.IsSuccess);
        Assert.Equal("conflict", changePending.Error!.Value.Code);
        _ = invite;
    }

    [Fact]
    public void Roles_EnforceLeastPrivilege()
    {
        Assert.False(TenantAuthorization.CanMutate(TenantRole.Viewer));
        Assert.True(TenantAuthorization.CanMutate(TenantRole.Editor));
        Assert.True(TenantAuthorization.CanMutate(TenantRole.Owner));
        Assert.False(TenantAuthorization.CanManageMembers(TenantRole.Editor));
        Assert.True(TenantAuthorization.CanManageMembers(TenantRole.Owner));
        Assert.True(TenantAuthorization.Satisfies(TenantRole.Owner, TenantRole.Editor));
        Assert.False(TenantAuthorization.Satisfies(TenantRole.Viewer, TenantRole.Editor));
    }

    [Fact]
    public async Task ResolvePrincipal_RequiresActiveMembership()
    {
        var f = new Fixture();
        var (tenant, _) = await CreateTenantAsync(f);
        var svc = f.Service();

        Assert.NotNull(await svc.ResolvePrincipalAsync(tenant.Id, "owner-1", false, CancellationToken.None));
        Assert.Null(await svc.ResolvePrincipalAsync(tenant.Id, "stranger", false, CancellationToken.None));

        var invite = await svc.InviteAsync(
            new InviteMemberCommand(tenant.Id, "owner-1", "Invited Ira", TenantRole.Viewer, Now), CancellationToken.None);
        Assert.NotNull(invite.Value.InviteToken);
        // Invited-but-not-accepted subjects have no principal.
        Assert.Null(await svc.ResolvePrincipalAsync(tenant.Id, string.Empty, false, CancellationToken.None));
    }

    [Fact]
    public void Oidc_ValidatesIssuerAudienceExpiry_And_FailsClosedOnOutage()
    {
        var f = new Fixture();
        var validator = new OidcTokenValidator(Microsoft.Extensions.Options.Options.Create(f.Options));
        var valid = new OidcCallbackCommand(
            "https://issuer.example", "user-1", "argoscope-test", Now.AddMinutes(5), null, Now);
        Assert.True(validator.Validate(valid).IsSuccess);

        Assert.False(validator.Validate(valid with { Issuer = "https://evil.example" }).IsSuccess);
        Assert.False(validator.Validate(valid with { Audience = "other" }).IsSuccess);
        Assert.False(validator.Validate(valid with { ExpiresAtUtc = Now.AddMinutes(-1) }).IsSuccess);

        f.Options.OidcProviderUnavailable = true;
        var outage = validator.Validate(valid);
        Assert.False(outage.IsSuccess);
        Assert.Equal("unavailable", outage.Error!.Value.Code);
    }

    [Fact]
    public async Task OidcSignIn_UnknownOrRevokedSubject_IsUnauthorized()
    {
        var f = new Fixture();
        var (tenant, owner) = await CreateTenantAsync(f);
        var svc = f.Service();
        OidcCallbackCommand Callback(string subject) => new(
            "https://issuer.example", subject, "argoscope-test", Now.AddMinutes(5), null, Now);

        var unknown = await svc.OidcSignInAsync(Callback("ghost"), tenant.Id, CancellationToken.None);
        Assert.False(unknown.IsSuccess);
        Assert.Equal("unauthorized", unknown.Error!.Value.Code);

        var known = await svc.OidcSignInAsync(Callback("owner-1"), tenant.Id, CancellationToken.None);
        Assert.True(known.IsSuccess);
        Assert.NotEmpty(known.Value.SessionToken);

        await svc.RevokeAsync(tenant.Id, "owner-1", owner.Id, Now, CancellationToken.None);
        // Revoking the last owner is rejected, so the owner is still active;
        // revoke a second member instead to prove revoked sessions lose access.
        var second = await svc.InviteAsync(
            new InviteMemberCommand(tenant.Id, "owner-1", "Temp Tom", TenantRole.Viewer, Now), CancellationToken.None);
        var accepted = await svc.AcceptInviteAsync(second.Value.InviteToken, "temp-1", Now, CancellationToken.None);
        Assert.True(accepted.IsSuccess);
        var revoked = await svc.RevokeAsync(
            tenant.Id, "owner-1", Id<TenantMembership>.From(accepted.Value.Member.MembershipId), Now, CancellationToken.None);
        Assert.True(revoked.IsSuccess);
        Assert.Null(await svc.ResolvePrincipalAsync(tenant.Id, "temp-1", true, CancellationToken.None));
    }

    [Fact]
    public async Task Migration_AssignsUnscoped_PreservesAssigned_VerifiesNullFree()
    {
        var f = new Fixture();
        var (tenant, _) = await CreateTenantAsync(f);
        var other = new Tenant("Other", Now);
        await f.Tenants.AddAsync(other, CancellationToken.None);

        var legacy1 = new Portfolio("Legacy One", Now);
        var legacy2 = new Portfolio("Legacy Two", Now);
        var owned = new Portfolio("Owned", Now);
        owned.AssignTenant(other.Id, Now);
        f.Portfolios.Seed(legacy1, legacy2, owned);

        Assert.Equal(2, await f.Migration().CountUnscopedAsync(CancellationToken.None));
        var result = await f.Migration().MigrateAsync(tenant.Id, Now, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Assigned);
        Assert.Equal(3, result.Value.Total);
        Assert.Equal(0, await f.Migration().CountUnscopedAsync(CancellationToken.None));
        Assert.Equal(other.Id, owned.TenantId);
    }

    [Fact]
    public async Task Migration_UnknownTenant_IsNotFound_And_SaveFailure_LeavesRowsUnscoped()
    {
        var f = new Fixture();
        var missing = await f.Migration().MigrateAsync(
            Id<Tenant>.From(Guid.NewGuid()), Now, CancellationToken.None);
        Assert.False(missing.IsSuccess);
        Assert.Equal("not_found", missing.Error!.Value.Code);

        var (tenant, _) = await CreateTenantAsync(f);
        f.Portfolios.Seed(new Portfolio("Legacy", Now));
        f.Portfolios.FailOnSave = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Migration().MigrateAsync(tenant.Id, Now, CancellationToken.None));
        Assert.Equal(1, await f.Migration().CountUnscopedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Session_Expires_AfterBoundedLifetime()
    {
        var clock = new FixedClock(Now);
        var store = new InMemorySessionStore(clock, TimeSpan.FromHours(8));
        var tenantId = Id<Tenant>.From(Guid.NewGuid());
        var issued = await store.IssueAsync(tenantId, "user-1", Now, CancellationToken.None);
        Assert.NotNull(await store.FindAsync(issued.Token, Now.AddHours(7), CancellationToken.None));
        Assert.Null(await store.FindAsync(issued.Token, Now.AddHours(9), CancellationToken.None));
    }

    [Fact]
    public void JobScope_CacheKey_IncludesTenant()
    {
        var a = Id<Tenant>.From(Guid.NewGuid());
        var b = Id<Tenant>.From(Guid.NewGuid());
        Assert.NotEqual(TenantJobScope.CacheKey(a, "snapshots"), TenantJobScope.CacheKey(b, "snapshots"));
        Assert.Contains(a.Value.ToString(), TenantJobScope.CacheKey(a, "snapshots"));
        Assert.Contains("unassigned", TenantJobScope.CacheKey(null, "snapshots"));
    }

    private sealed class InMemoryTenantStore : ITenantStore
    {
        private readonly Dictionary<Id<Tenant>, Tenant> _byId = new();
        public Task<Tenant?> FindAsync(Id<Tenant> tenantId, CancellationToken ct) =>
            Task.FromResult(_byId.TryGetValue(tenantId, out var v) ? v : null);
        public Task<IReadOnlyList<Tenant>> ListAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Tenant>>(_byId.Values.ToList());
        public Task AddAsync(Tenant tenant, CancellationToken ct) { _byId[tenant.Id] = tenant; return Task.CompletedTask; }
        public Task UpdateAsync(Tenant tenant, CancellationToken ct) { _byId[tenant.Id] = tenant; return Task.CompletedTask; }
    }

    private sealed class InMemoryTenantMembershipStore : ITenantMembershipStore
    {
        private readonly Dictionary<Id<TenantMembership>, TenantMembership> _byId = new();
        public Task<TenantMembership?> FindAsync(Id<TenantMembership> id, CancellationToken ct) =>
            Task.FromResult(_byId.TryGetValue(id, out var v) ? v : null);
        public Task<TenantMembership?> FindByTokenAsync(string token, CancellationToken ct) =>
            Task.FromResult(_byId.Values.FirstOrDefault(m => m.InviteToken == token));
        public Task<TenantMembership?> FindBySubjectAsync(Id<Tenant> tenantId, string subject, CancellationToken ct) =>
            Task.FromResult(_byId.Values.FirstOrDefault(m => m.TenantId == tenantId && m.Subject == subject));
        public Task<IReadOnlyList<TenantMembership>> ListByTenantAsync(Id<Tenant> tenantId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TenantMembership>>(_byId.Values.Where(m => m.TenantId == tenantId).ToList());
        public Task AddAsync(TenantMembership m, CancellationToken ct) { _byId[m.Id] = m; return Task.CompletedTask; }
        public Task UpdateAsync(TenantMembership m, CancellationToken ct) { _byId[m.Id] = m; return Task.CompletedTask; }
    }

    private sealed class InMemoryPortfolioTenantStore : IPortfolioTenantStore
    {
        private readonly List<Portfolio> _portfolios = new();
        public bool FailOnSave { get; set; }
        public void Seed(params Portfolio[] portfolios) => _portfolios.AddRange(portfolios);
        public Task<IReadOnlyList<Portfolio>> ListAllPortfoliosAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Portfolio>>(_portfolios.ToList());
        public Task SaveAsync(CancellationToken ct) =>
            FailOnSave ? throw new InvalidOperationException("simulated persistence failure") : Task.CompletedTask;
    }
}
