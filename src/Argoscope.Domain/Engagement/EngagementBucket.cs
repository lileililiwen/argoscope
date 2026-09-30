using Argoscope.Domain.Common;
using Argoscope.Domain.Memberships;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;

namespace Argoscope.Domain.Engagement;

/// <summary>
/// One day's bucket of engagement activity for a repository, split by author
/// attribution. Owner is identified by the repository's owner login; external
/// is everyone else; unknown is the author whose identity the provider could
/// not resolve.
/// </summary>
public sealed class EngagementBucket : Entity<Id<EngagementBucket>>
{
    public Id<Repository> RepositoryId { get; private set; }
    public DateOnly BucketDate { get; private set; }

    public int ExternalIssuesOpened { get; private set; }
    public int ExternalPullRequestsOpened { get; private set; }
    public int ExternalContributors { get; private set; }

    public int OwnerIssuesOpened { get; private set; }
    public int OwnerPullRequestsOpened { get; private set; }
    public int OwnerContributors { get; private set; }

    public int UnknownIssuesOpened { get; private set; }
    public int UnknownPullRequestsOpened { get; private set; }
    public int UnknownContributors { get; private set; }

    public DateTimeOffset ObservedAtUtc { get; private set; }
    public DateTimeOffset CollectedAtUtc { get; private set; }

    private EngagementBucket() : base() { }

    public EngagementBucket(
        Id<Repository> repositoryId,
        DateOnly bucketDate,
        int externalIssuesOpened,
        int externalPullRequestsOpened,
        int externalContributors,
        int ownerIssuesOpened,
        int ownerPullRequestsOpened,
        int ownerContributors,
        int unknownIssuesOpened,
        int unknownPullRequestsOpened,
        int unknownContributors,
        DateTimeOffset observedAtUtc,
        DateTimeOffset collectedAtUtc)
        : base(Id<EngagementBucket>.New())
    {
        if (externalIssuesOpened < 0 || externalPullRequestsOpened < 0 || externalContributors < 0
            || ownerIssuesOpened < 0 || ownerPullRequestsOpened < 0 || ownerContributors < 0
            || unknownIssuesOpened < 0 || unknownPullRequestsOpened < 0 || unknownContributors < 0)
        {
            throw new DomainException("validation", "Engagement counts must be non-negative.");
        }

        RepositoryId = repositoryId;
        BucketDate = bucketDate;
        ExternalIssuesOpened = externalIssuesOpened;
        ExternalPullRequestsOpened = externalPullRequestsOpened;
        ExternalContributors = externalContributors;
        OwnerIssuesOpened = ownerIssuesOpened;
        OwnerPullRequestsOpened = ownerPullRequestsOpened;
        OwnerContributors = ownerContributors;
        UnknownIssuesOpened = unknownIssuesOpened;
        UnknownPullRequestsOpened = unknownPullRequestsOpened;
        UnknownContributors = unknownContributors;
        ObservedAtUtc = observedAtUtc;
        CollectedAtUtc = collectedAtUtc;
    }
}
