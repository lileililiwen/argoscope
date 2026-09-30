using System.Security.Cryptography;
using Argoscope.Domain.Common;

namespace Argoscope.Domain.Identity;

/// <summary>Membership capability. Owner manages members; Editor mutates portfolio data; Viewer reads only.</summary>
public enum TenantRole
{
    Viewer = 0,
    Editor = 1,
    Owner = 2,
}

/// <summary>Lifecycle of one account membership in a tenant.</summary>
public enum MembershipState
{
    Invited = 0,
    Active = 1,
    Revoked = 2,
}

/// <summary>
/// One account membership in a tenant. The external identity is the immutable
/// OIDC subject (never a stored password); invites are single-use tokens that
/// expire 7 days after issue. State transitions are explicit so invite replay,
/// expiry and the last-owner invariant are observable.
/// </summary>
public sealed class TenantMembership : Entity<Id<TenantMembership>>
{
    public const int MaxSubjectLength = 300;
    public const int MaxDisplayNameLength = 200;
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);

    public Id<Tenant> TenantId { get; private set; }

    /// <summary>Immutable external identity (OIDC subject). Empty while invited by display name only.</summary>
    public string Subject { get; private set; }

    public string DisplayName { get; private set; }

    public TenantRole Role { get; private set; }

    public MembershipState State { get; private set; }

    /// <summary>Single-use invite token; null once accepted or revoked.</summary>
    public string? InviteToken { get; private set; }

    public DateTimeOffset? InviteExpiresAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private TenantMembership() : base()
    {
        TenantId = default!;
        Subject = string.Empty;
        DisplayName = string.Empty;
    }

    private TenantMembership(
        Id<Tenant> tenantId,
        string subject,
        string displayName,
        TenantRole role,
        MembershipState state,
        string? inviteToken,
        DateTimeOffset? inviteExpiresAtUtc,
        DateTimeOffset now)
        : base(Id<TenantMembership>.New())
    {
        TenantId = tenantId;
        Subject = subject;
        DisplayName = displayName;
        Role = role;
        State = state;
        InviteToken = inviteToken;
        InviteExpiresAtUtc = inviteExpiresAtUtc;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    /// <summary>Create the first owner (active immediately, no invite token).</summary>
    public static TenantMembership CreateOwner(
        Id<Tenant> tenantId, string subject, string displayName, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new DomainException("validation", "Owner subject is required.");
        }

        return new TenantMembership(
            tenantId,
            subject.Trim(),
            string.IsNullOrWhiteSpace(displayName) ? subject.Trim() : displayName.Trim(),
            TenantRole.Owner,
            MembershipState.Active,
            null,
            null,
            now);
    }

    /// <summary>Create a single-use, expiring invite bound to a role.</summary>
    public static TenantMembership CreateInvite(
        Id<Tenant> tenantId, string displayName, TenantRole role, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new DomainException("validation", "Invite display name is required.");
        }

        var token = GenerateInviteToken();
        return new TenantMembership(
            tenantId,
            string.Empty,
            displayName.Trim(),
            role,
            MembershipState.Invited,
            token,
            now.Add(InviteLifetime),
            now);
    }

    /// <summary>Accept a pending invite. The token is consumed (single-use); replay is rejected.</summary>
    public void Accept(string token, string subject, DateTimeOffset now)
    {
        if (State != MembershipState.Invited)
        {
            throw new DomainException("conflict", "Invite has already been used or revoked.");
        }

        if (InviteToken is null || !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(InviteToken),
                System.Text.Encoding.UTF8.GetBytes(token ?? string.Empty)))
        {
            throw new DomainException("not_found", "Invite token is unknown.");
        }

        if (InviteExpiresAtUtc.HasValue && now > InviteExpiresAtUtc.Value)
        {
            throw new DomainException("conflict", "Invite has expired.");
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new DomainException("validation", "Subject is required to accept an invite.");
        }

        Subject = subject.Trim();
        State = MembershipState.Active;
        InviteToken = null;
        InviteExpiresAtUtc = null;
        UpdatedAtUtc = now;
    }

    public void Revoke(DateTimeOffset now)
    {
        if (State == MembershipState.Revoked)
        {
            throw new DomainException("conflict", "Membership is already revoked.");
        }

        State = MembershipState.Revoked;
        InviteToken = null;
        InviteExpiresAtUtc = null;
        UpdatedAtUtc = now;
    }

    public void ChangeRole(TenantRole role, DateTimeOffset now)
    {
        if (State != MembershipState.Active)
        {
            throw new DomainException("conflict", "Only active memberships can change role.");
        }

        Role = role;
        UpdatedAtUtc = now;
    }

    private static string GenerateInviteToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
