using Argoscope.Domain.Common;
using Argoscope.Domain.Identity;
using Microsoft.Extensions.Options;

namespace Argoscope.Application.Identity;

/// <summary>
/// Tenant and membership lifecycle: first-owner tenant creation, single-use
/// expiring invites, accept/revoke/role-change with the last-owner invariant.
/// Every membership mutation is owner-managed; editors mutate portfolio data;
/// viewers read.
/// </summary>
public sealed class TenantService
{
    private readonly ITenantStore _tenants;
    private readonly ITenantMembershipStore _memberships;
    private readonly ISessionStore _sessions;
    private readonly OidcTokenValidator _oidc;
    private readonly IdentityOptions _options;

    public TenantService(
        ITenantStore tenants,
        ITenantMembershipStore memberships,
        ISessionStore sessions,
        OidcTokenValidator oidc,
        IOptions<IdentityOptions> options)
    {
        _tenants = tenants;
        _memberships = memberships;
        _sessions = sessions;
        _oidc = oidc;
        _options = options.Value;
    }

    public async Task<Result<(TenantDto Tenant, TenantMemberDto Owner)>> CreateTenantAsync(
        CreateTenantCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Error.Validation("Tenant name is required.");
        }

        if (string.IsNullOrWhiteSpace(command.OwnerSubject))
        {
            return Error.Validation("Owner subject is required.");
        }

        var tenant = new Tenant(command.Name, command.Now);
        await _tenants.AddAsync(tenant, cancellationToken).ConfigureAwait(false);
        var owner = TenantMembership.CreateOwner(
            tenant.Id, command.OwnerSubject.Trim(), command.OwnerDisplayName ?? command.OwnerSubject, command.Now);
        await _memberships.AddAsync(owner, cancellationToken).ConfigureAwait(false);
        return (TenantDtos.ToDto(tenant), TenantDtos.ToDto(owner));
    }

    public async Task<TenantDto?> GetTenantAsync(Id<Tenant> tenantId, CancellationToken cancellationToken)
    {
        var tenant = await _tenants.FindAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return tenant is null ? null : TenantDtos.ToDto(tenant);
    }

    /// <summary>Resolve an active principal; returns null when the subject has
    /// no active membership (unknown, invited-but-not-accepted, or revoked).</summary>
    public async Task<TenantPrincipal?> ResolvePrincipalAsync(
        Id<Tenant> tenantId, string subject, bool viaCookie, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subject)) return null;
        var membership = await _memberships
            .FindBySubjectAsync(tenantId, subject.Trim(), cancellationToken).ConfigureAwait(false);
        if (membership is null || membership.State != MembershipState.Active) return null;
        return new TenantPrincipal(tenantId, membership.Subject, membership.Role, viaCookie);
    }

    public async Task<IReadOnlyList<TenantMemberDto>> ListMembersAsync(
        Id<Tenant> tenantId, CancellationToken cancellationToken)
    {
        var members = await _memberships.ListByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return members.Select(TenantDtos.ToDto).ToList();
    }

    public async Task<Result<(TenantMemberDto Member, string InviteToken)>> InviteAsync(
        InviteMemberCommand command, CancellationToken cancellationToken)
    {
        var ownerError = await RequireOwnerAsync(command.TenantId, command.ActorSubject, cancellationToken).ConfigureAwait(false);
        if (ownerError.HasValue) return ownerError.Value;

        var tenant = await _tenants.FindAsync(command.TenantId, cancellationToken).ConfigureAwait(false);
        if (tenant is null) return Error.NotFound("Tenant not found.");

        TenantMembership invite;
        try
        {
            invite = TenantMembership.CreateInvite(command.TenantId, command.DisplayName, command.Role, command.Now);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        await _memberships.AddAsync(invite, cancellationToken).ConfigureAwait(false);
        return (TenantDtos.ToDto(invite), invite.InviteToken!);
    }

    /// <summary>Accept an invite token. Returns the member plus a bounded
    /// session token for the secure HttpOnly cookie.</summary>
    public async Task<Result<InviteAccepted>> AcceptInviteAsync(
        string token, string subject, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token)) return Error.NotFound("Invite token is unknown.");
        var invite = await _memberships.FindByTokenAsync(token, cancellationToken).ConfigureAwait(false);
        if (invite is null) return Error.NotFound("Invite token is unknown.");

        try
        {
            invite.Accept(token, subject, now);
        }
        catch (DomainException ex)
        {
            return ex.Code switch
            {
                "not_found" => Error.NotFound(ex.Message),
                "conflict" => Error.Conflict(ex.Message),
                _ => Error.Validation(ex.Message),
            };
        }

        await _memberships.UpdateAsync(invite, cancellationToken).ConfigureAwait(false);
        var session = await _sessions.IssueAsync(invite.TenantId, invite.Subject, now, cancellationToken).ConfigureAwait(false);
        return new InviteAccepted(TenantDtos.ToDto(invite), session.Token, session.ExpiresAtUtc);
    }

    public async Task<Result<TenantMemberDto>> RevokeAsync(
        Id<Tenant> tenantId, string actorSubject, Id<TenantMembership> membershipId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var ownerError = await RequireOwnerAsync(tenantId, actorSubject, cancellationToken).ConfigureAwait(false);
        if (ownerError.HasValue) return ownerError.Value;

        var target = await _memberships.FindAsync(membershipId, cancellationToken).ConfigureAwait(false);
        if (target is null || target.TenantId != tenantId) return Error.NotFound("Membership not found.");

        if (await WouldRemoveLastOwnerAsync(tenantId, target, newRole: null, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict("Operation would leave no active Owner.");
        }

        try
        {
            target.Revoke(now);
        }
        catch (DomainException ex)
        {
            return Error.Conflict(ex.Message);
        }

        await _memberships.UpdateAsync(target, cancellationToken).ConfigureAwait(false);
        return TenantDtos.ToDto(target);
    }

    public async Task<Result<TenantMemberDto>> ChangeRoleAsync(
        Id<Tenant> tenantId, string actorSubject, Id<TenantMembership> membershipId,
        TenantRole role, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var ownerError = await RequireOwnerAsync(tenantId, actorSubject, cancellationToken).ConfigureAwait(false);
        if (ownerError.HasValue) return ownerError.Value;

        var target = await _memberships.FindAsync(membershipId, cancellationToken).ConfigureAwait(false);
        if (target is null || target.TenantId != tenantId) return Error.NotFound("Membership not found.");

        if (await WouldRemoveLastOwnerAsync(tenantId, target, role, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict("Operation would leave no active Owner.");
        }

        try
        {
            target.ChangeRole(role, now);
        }
        catch (DomainException ex)
        {
            return Error.Conflict(ex.Message);
        }

        await _memberships.UpdateAsync(target, cancellationToken).ConfigureAwait(false);
        return TenantDtos.ToDto(target);
    }

    /// <summary>OIDC sign-in: validate claims against deployment config, then
    /// map the immutable subject to an active membership and issue a bounded
    /// session. Unknown subjects are rejected (membership requires an invite).</summary>
    public async Task<Result<InviteAccepted>> OidcSignInAsync(
        OidcCallbackCommand command, Id<Tenant> tenantId, CancellationToken cancellationToken)
    {
        var valid = _oidc.Validate(command);
        if (!valid.IsSuccess) return valid.Error!.Value;

        var membership = await _memberships
            .FindBySubjectAsync(tenantId, command.Subject.Trim(), cancellationToken).ConfigureAwait(false);
        if (membership is null || membership.State != MembershipState.Active)
        {
            return Error.Unauthorized("Account is not a member of this tenant.");
        }

        var session = await _sessions.IssueAsync(tenantId, membership.Subject, command.Now, cancellationToken).ConfigureAwait(false);
        return new InviteAccepted(TenantDtos.ToDto(membership), session.Token, session.ExpiresAtUtc);
    }

    private async Task<Error?> RequireOwnerAsync(
        Id<Tenant> tenantId, string actorSubject, CancellationToken cancellationToken)
    {
        if (!_options.IsHosted)
        {
            // SingleOwner deployment profile: the local owner maps to a
            // tenant without external OIDC; the API filter already bypasses
            // tenant enforcement in this mode.
            return null;
        }

        var actor = await _memberships
            .FindBySubjectAsync(tenantId, actorSubject?.Trim() ?? string.Empty, cancellationToken).ConfigureAwait(false);
        if (actor is null || actor.State != MembershipState.Active)
        {
            return Error.Unauthorized("Authentication is required.");
        }

        if (actor.Role != TenantRole.Owner)
        {
            return Error.Forbidden("Membership management requires the Owner role.");
        }

        return null;
    }

    private async Task<bool> WouldRemoveLastOwnerAsync(
        Id<Tenant> tenantId, TenantMembership target, TenantRole? newRole, CancellationToken cancellationToken)
    {
        if (target.Role != TenantRole.Owner) return false;
        if (target.State != MembershipState.Active) return false;
        // Revoke (newRole null) or downgrade removes one active owner.
        if (newRole == TenantRole.Owner) return false;
        var members = await _memberships.ListByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var activeOwners = members.Count(m =>
            m.State == MembershipState.Active && m.Role == TenantRole.Owner && m.Id != target.Id);
        return activeOwners == 0;
    }
}
