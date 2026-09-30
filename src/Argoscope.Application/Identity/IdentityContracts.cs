using Argoscope.Domain.Common;
using Argoscope.Domain.Identity;

namespace Argoscope.Application.Identity;

/// <summary>Authenticated tenant principal resolved per request.</summary>
public sealed record TenantPrincipal(
    Id<Tenant> TenantId,
    string Subject,
    TenantRole Role,
    bool ViaCookie);

/// <summary>Read models for the tenant API surface.</summary>
public sealed record TenantDto(
    Guid TenantId,
    string Name,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>Read models for the membership API surface. Invite tokens are never exposed.</summary>
public sealed record TenantMemberDto(
    Guid MembershipId,
    Guid TenantId,
    string Subject,
    string DisplayName,
    string Role,
    string State,
    DateTimeOffset? InviteExpiresAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreateTenantCommand(
    string Name,
    string OwnerSubject,
    string? OwnerDisplayName,
    DateTimeOffset Now);

public sealed record InviteMemberCommand(
    Id<Tenant> TenantId,
    string ActorSubject,
    string DisplayName,
    TenantRole Role,
    DateTimeOffset Now);

public sealed record InviteAccepted(
    TenantMemberDto Member,
    string SessionToken,
    DateTimeOffset SessionExpiresAtUtc);

/// <summary>OIDC callback payload validated against deployment config.
/// Signature validation happens at the deployment OIDC middleware boundary;
/// Argoscope enforces issuer/audience/expiry plus membership server-side and
/// never stores passwords.</summary>
public sealed record OidcCallbackCommand(
    string Issuer,
    string Subject,
    string Audience,
    DateTimeOffset ExpiresAtUtc,
    string? DisplayName,
    DateTimeOffset Now);

public static class TenantDtos
{
    public static TenantDto ToDto(Tenant tenant) =>
        new(tenant.Id.Value, tenant.Name, tenant.CreatedAtUtc, tenant.UpdatedAtUtc);

    public static TenantMemberDto ToDto(TenantMembership membership) =>
        new(
            membership.Id.Value,
            membership.TenantId.Value,
            membership.Subject,
            membership.DisplayName,
            membership.Role.ToString(),
            membership.State.ToString(),
            membership.InviteExpiresAtUtc,
            membership.CreatedAtUtc,
            membership.UpdatedAtUtc);
}
