using System.Text.Json.Serialization;
using Argoscope.Domain.Common;

namespace Argoscope.Domain.Repositories;

/// <summary>
/// Visibility observation returned by the provider for a single repository.
/// Argoscope never stores the source token or the response body; only the
/// normalized value plus the provider status and freshness metadata.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RepositoryVisibility
{
    Public = 0,
    Private = 1,
    Internal = 2,
    Unknown = 3,
}

/// <summary>
/// Argoscope-owned representation of a GitHub repository. The identity is the
/// stable GraphQL node id (so renames and transfers do not duplicate the
/// record); the locator (owner/name) is a display field that can change over
/// time.
/// </summary>
public sealed class Repository : Entity<Id<Repository>>
{
    /// <summary>Stable GitHub node id (GraphQL global id). Unique per repo across renames.</summary>
    public string NodeId { get; private set; }

    public string OwnerLogin { get; private set; }

    public string Name { get; private set; }

    public string FullName => $"{OwnerLogin}/{Name}";

    public RepositoryVisibility Visibility { get; private set; }

    public DateOnly? CreatedOnGithubAt { get; private set; }

    public string? PrimaryLanguage { get; private set; }

    public DateTimeOffset? LastActivityAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private Repository() : base() { }

    /// <summary>
    /// Creates a new repository. <paramref name="nodeId"/> is the stable
    /// identity that the rest of the system uses to deduplicate observations
    /// across renames and transfers.
    /// </summary>
    public Repository(
        string nodeId,
        string ownerLogin,
        string name,
        RepositoryVisibility visibility,
        DateTimeOffset now,
        DateOnly? createdOnGithubAt = null,
        string? primaryLanguage = null,
        DateTimeOffset? lastActivityAtUtc = null)
        : base(Id<Repository>.New())
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            throw new DomainException("validation", "Repository node id is required.");
        }
        if (string.IsNullOrWhiteSpace(ownerLogin))
        {
            throw new DomainException("validation", "Repository owner login is required.");
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("validation", "Repository name is required.");
        }

        NodeId = nodeId.Trim();
        OwnerLogin = ownerLogin.Trim();
        Name = name.Trim();
        Visibility = visibility;
        CreatedOnGithubAt = createdOnGithubAt;
        PrimaryLanguage = primaryLanguage?.Trim();
        LastActivityAtUtc = lastActivityAtUtc;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    /// <summary>
    /// Updates the display locator when GitHub reports a new owner/name for
    /// the same node id. Does not change identity.
    /// </summary>
    public void UpdateLocator(
        string ownerLogin,
        string name,
        RepositoryVisibility visibility,
        DateTimeOffset now,
        DateOnly? createdOnGithubAt,
        string? primaryLanguage,
        DateTimeOffset? lastActivityAtUtc)
    {
        if (string.IsNullOrWhiteSpace(ownerLogin))
        {
            throw new DomainException("validation", "Repository owner login is required.");
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("validation", "Repository name is required.");
        }

        OwnerLogin = ownerLogin.Trim();
        Name = name.Trim();
        Visibility = visibility;
        if (createdOnGithubAt.HasValue)
        {
            CreatedOnGithubAt = createdOnGithubAt;
        }
        PrimaryLanguage = primaryLanguage?.Trim();
        LastActivityAtUtc = lastActivityAtUtc;
        UpdatedAtUtc = now;
    }
}
