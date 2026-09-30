using Argoscope.Application.Signals;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Signals;
using Argoscope.GitHub;
using Xunit;

namespace Argoscope.UnitTests;

public sealed class SignalTextProcessorTests
{
    [Fact]
    public void RedactsEmailAndToken()
    {
        var excerpt = SignalTextProcessor.BuildExcerpt(
            "Need hosted version",
            "Contact me at owner@example.com with token ghp_abc123XYZ please.");
        Assert.DoesNotContain("owner@example.com", excerpt);
        Assert.DoesNotContain("ghp_abc123XYZ", excerpt);
        Assert.Contains("[redacted-email]", excerpt);
        Assert.Contains("[redacted-token]", excerpt);
        Assert.Contains("Need hosted version", excerpt);
    }

    [Fact]
    public void TruncatesTo4000Chars()
    {
        var excerpt = SignalTextProcessor.BuildExcerpt("t", new string('x', 5000));
        Assert.True(excerpt.Length <= CommercialSignal.MaxExcerptLength);
        Assert.Equal(CommercialSignal.MaxExcerptLength, excerpt.Length);
    }

    [Fact]
    public void EmptyTitleAndBodyHasNoUsableText()
    {
        Assert.False(SignalTextProcessor.HasUsableText("", "   "));
        Assert.True(SignalTextProcessor.HasUsableText("hi", ""));
    }

    [Fact]
    public void ContentHashIsStableAndDistinct()
    {
        var at = DateTimeOffset.UtcNow;
        var a = SignalTextProcessor.ContentHash("Issue", 1, at, "same");
        var b = SignalTextProcessor.ContentHash("Issue", 1, at, "same");
        var c = SignalTextProcessor.ContentHash("Issue", 1, at, "different");
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }
}

public sealed class KeywordSignalClassifierTests
{
    private readonly KeywordSignalClassifier _sut = new();

    [Theory]
    [InlineData("Can you offer a hosted version for our team?", "HostedRequest")]
    [InlineData("We need a paid support contract with SLA", "PaidSupport")]
    [InlineData("Do you support SSO via SAML and audit logs?", "EnterpriseCapability")]
    [InlineData("Our procurement team needs a security questionnaire and MSA", "ProcurementQuestion")]
    [InlineData("Bug: stack trace on startup, here is a repro", "NotCommercial")]
    [InlineData("Just saying hello, love the project", "Unclear")]
    public async Task ClassifiesKnownCategories(string text, string expected)
    {
        var result = await _sut.ClassifyAsync(text, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Classification);
        Assert.Equal(expected, result.Classification!.Category.ToString());
        Assert.InRange(result.Classification.Confidence, 0.0, 1.0);
    }

    [Fact]
    public async Task LowConfidenceDefaultsToUnclear()
    {
        var stub = new LowConfidenceClassifier();
        var service = SignalFixture.CreateService(classifier: stub);
        service.Sources.Sources.Add(new CommercialSourceInput(
            SignalSourceType.Issue, 7, "https://example.test/i/7",
            DateTimeOffset.UtcNow, "hosted?", "maybe hosted someday"));
        var result = await service.Service.CollectAsync(
            new CollectSignalsCommand(service.Repository.Id, DateTimeOffset.UtcNow), CancellationToken.None);
        Assert.True(result.IsSuccess);
        var list = await service.Service.ListAsync(service.Repository.Id, null, CancellationToken.None);
        Assert.Single(list);
        Assert.Equal("Unclear", list[0].Category);
    }
}

public sealed class CommercialSignalServiceTests
{
    [Fact]
    public async Task CollectCreatesClassifyAndSkipsEmpty()
    {
        var f = SignalFixture.CreateService();
        f.Sources.Sources.Add(new CommercialSourceInput(
            SignalSourceType.Issue, 1, "https://example.test/i/1",
            DateTimeOffset.UtcNow, "Hosted version?", "We want managed hosting for our team."));
        f.Sources.Sources.Add(new CommercialSourceInput(
            SignalSourceType.PullRequest, 2, "https://example.test/pr/2",
            DateTimeOffset.UtcNow, "", "   "));
        var result = await f.Service.CollectAsync(
            new CollectSignalsCommand(f.Repository.Id, DateTimeOffset.UtcNow), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Created);
        Assert.Equal("HostedRequest", (await f.Service.ListAsync(f.Repository.Id, null, CancellationToken.None)).Single().Category);
        Assert.Equal(1, f.Sources.Calls);
    }

    [Fact]
    public async Task DuplicateJobIsIdempotent()
    {
        var f = SignalFixture.CreateService();
        var at = DateTimeOffset.UtcNow;
        f.Sources.Sources.Add(new CommercialSourceInput(
            SignalSourceType.Issue, 3, "https://example.test/i/3", at,
            "Support contract", "We need a paid support plan."));
        var first = await f.Service.CollectAsync(new CollectSignalsCommand(f.Repository.Id, at), CancellationToken.None);
        var second = await f.Service.CollectAsync(new CollectSignalsCommand(f.Repository.Id, at), CancellationToken.None);
        Assert.True(first.IsSuccess && second.IsSuccess);
        Assert.Equal(1, first.Value.Created);
        Assert.Equal(1, second.Value.Duplicates);
        Assert.Single(await f.Service.ListAsync(f.Repository.Id, null, CancellationToken.None));
    }

    [Fact]
    public async Task ChangedContentCreatesNewVersionAndKeepsDecision()
    {
        var f = SignalFixture.CreateService();
        var at = DateTimeOffset.UtcNow;
        f.Sources.Sources.Add(new CommercialSourceInput(
            SignalSourceType.Issue, 4, "https://example.test/i/4", at,
            "Hosting?", "Do you offer managed hosting?"));
        await f.Service.CollectAsync(new CollectSignalsCommand(f.Repository.Id, at), CancellationToken.None);
        var first = (await f.Service.ListAsync(f.Repository.Id, null, CancellationToken.None)).Single();
        var reviewed = await f.Service.ReviewAsync(new ReviewSignalCommand(
            f.Repository.Id, Domain.Common.Id<CommercialSignal>.From(first.SignalId),
            first.Version, "Accept", null, null, "owner", at), CancellationToken.None);
        Assert.True(reviewed.IsSuccess);

        f.Sources.Sources.Clear();
        f.Sources.Sources.Add(new CommercialSourceInput(
            SignalSourceType.Issue, 4, "https://example.test/i/4", at.AddHours(1),
            "Hosting?", "Do you offer managed hosting plus SSO?"));
        var second = await f.Service.CollectAsync(
            new CollectSignalsCommand(f.Repository.Id, at.AddHours(1)), CancellationToken.None);
        Assert.True(second.IsSuccess);
        Assert.Equal(1, second.Value.Created);
        var all = await f.Service.ListAsync(f.Repository.Id, null, CancellationToken.None);
        Assert.Equal(2, all.Count);
        Assert.Contains(all, s => s.Status == "Accepted" && s.SuggestionVersion == 1);
        Assert.Contains(all, s => s.Status == "Pending" && s.SuggestionVersion == 2);
    }

    [Fact]
    public async Task MalformedOutputBecomesRetryableUnclassified()
    {
        var f = SignalFixture.CreateService(classifier: new MalformedClassifier());
        f.Sources.Sources.Add(new CommercialSourceInput(
            SignalSourceType.Issue, 5, "https://example.test/i/5",
            DateTimeOffset.UtcNow, "Hello", "Some body text here."));
        var result = await f.Service.CollectAsync(
            new CollectSignalsCommand(f.Repository.Id, DateTimeOffset.UtcNow), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Retried);
        var row = (await f.Service.ListAsync(f.Repository.Id, null, CancellationToken.None)).Single();
        Assert.Equal("Unclassified", row.Category);
        Assert.Equal("NeedsRetry", row.Status);
    }

    [Fact]
    public async Task ThrowingClassifierBecomesRetryableUnclassified()
    {
        var f = SignalFixture.CreateService(classifier: new ThrowingClassifier());
        f.Sources.Sources.Add(new CommercialSourceInput(
            SignalSourceType.Issue, 6, "https://example.test/i/6",
            DateTimeOffset.UtcNow, "Hello", "Some body text here."));
        var result = await f.Service.CollectAsync(
            new CollectSignalsCommand(f.Repository.Id, DateTimeOffset.UtcNow), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal("NeedsRetry", (await f.Service.ListAsync(f.Repository.Id, null, CancellationToken.None)).Single().Status);
    }

    [Fact]
    public async Task ReviewTransitionsAndConflict()
    {
        var f = SignalFixture.CreateService();
        f.Sources.Sources.Add(new CommercialSourceInput(
            SignalSourceType.Issue, 8, "https://example.test/i/8",
            DateTimeOffset.UtcNow, "Procurement", "Our procurement team asks for a DPA."));
        await f.Service.CollectAsync(new CollectSignalsCommand(f.Repository.Id, DateTimeOffset.UtcNow), CancellationToken.None);
        var row = (await f.Service.ListAsync(f.Repository.Id, null, CancellationToken.None)).Single();

        var corrected = await f.Service.ReviewAsync(new ReviewSignalCommand(
            f.Repository.Id, Domain.Common.Id<CommercialSignal>.From(row.SignalId),
            row.Version, "Correct", "PaidSupport", "actually support", "owner", DateTimeOffset.UtcNow),
            CancellationToken.None);
        Assert.True(corrected.IsSuccess);
        Assert.Equal("Corrected", corrected.Value.Status);
        Assert.Equal("PaidSupport", corrected.Value.CorrectedCategory);

        var stale = await f.Service.ReviewAsync(new ReviewSignalCommand(
            f.Repository.Id, Domain.Common.Id<CommercialSignal>.From(row.SignalId),
            row.Version, "Reject", null, null, "owner", DateTimeOffset.UtcNow),
            CancellationToken.None);
        Assert.True(stale.IsFailure);
        Assert.Equal("conflict", stale.Error!.Value.Code);

        var secondReview = await f.Service.ReviewAsync(new ReviewSignalCommand(
            f.Repository.Id, Domain.Common.Id<CommercialSignal>.From(row.SignalId),
            corrected.Value.Version, "Accept", null, null, "owner", DateTimeOffset.UtcNow),
            CancellationToken.None);
        Assert.True(secondReview.IsFailure);

        var reviews = await f.Service.GetReviewsAsync(
            f.Repository.Id, Domain.Common.Id<CommercialSignal>.From(row.SignalId), CancellationToken.None);
        Assert.Single(reviews);
        Assert.Equal("ProcurementQuestion", reviews[0].PriorCategory);
    }

    [Fact]
    public async Task DeletedSourceRetainsAuditAndMarksUnavailable()
    {
        var f = SignalFixture.CreateService();
        f.Sources.Sources.Add(new CommercialSourceInput(
            SignalSourceType.Issue, 9, "https://example.test/i/9",
            DateTimeOffset.UtcNow, "SSO?", "Do you support SAML SSO?"));
        await f.Service.CollectAsync(new CollectSignalsCommand(f.Repository.Id, DateTimeOffset.UtcNow), CancellationToken.None);
        var row = (await f.Service.ListAsync(f.Repository.Id, null, CancellationToken.None)).Single();
        await f.Service.ReviewAsync(new ReviewSignalCommand(
            f.Repository.Id, Domain.Common.Id<CommercialSignal>.From(row.SignalId),
            row.Version, "Accept", null, null, "owner", DateTimeOffset.UtcNow), CancellationToken.None);

        f.Sources.Sources.Clear();
        var result = await f.Service.CollectAsync(
            new CollectSignalsCommand(f.Repository.Id, DateTimeOffset.UtcNow), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.MarkedUnavailable);
        var after = (await f.Service.ListAsync(f.Repository.Id, null, CancellationToken.None)).Single();
        Assert.False(after.SourceAvailable);
        Assert.Equal("Accepted", after.Status);
        Assert.Single(await f.Service.GetReviewsAsync(
            f.Repository.Id, Domain.Common.Id<CommercialSignal>.From(after.SignalId), CancellationToken.None));
    }

    [Fact]
    public async Task CollectNeverWritesToGitHub()
    {
        var fake = new FakeGitHubRepositoryProvider();
        var f = SignalFixture.CreateService(sourceProvider: fake);
        fake.AddSource(new CommercialSourceInput(
            SignalSourceType.Issue, 10, "https://example.test/i/10",
            DateTimeOffset.UtcNow, "Bug", "A stack trace repro"), "owner", "repo");
        var result = await f.Service.CollectAsync(
            new CollectSignalsCommand(f.Repository.Id, DateTimeOffset.UtcNow), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Single(fake.SourceCalls);
        Assert.Empty(fake.RepositoryCalls);
        Assert.Empty(fake.MetricCalls);
        Assert.Empty(fake.EngagementCalls);
    }

    [Fact]
    public async Task StateFilterSeparatesPendingAndReviewed()
    {
        var f = SignalFixture.CreateService();
        f.Sources.Sources.Add(new CommercialSourceInput(
            SignalSourceType.Issue, 11, "https://example.test/i/11",
            DateTimeOffset.UtcNow, "Hosting?", "managed hosting please"));
        f.Sources.Sources.Add(new CommercialSourceInput(
            SignalSourceType.Issue, 12, "https://example.test/i/12",
            DateTimeOffset.UtcNow, "Typo", "docs typo in readme"));
        await f.Service.CollectAsync(new CollectSignalsCommand(f.Repository.Id, DateTimeOffset.UtcNow), CancellationToken.None);
        var pending = await f.Service.ListAsync(f.Repository.Id, "pending", CancellationToken.None);
        Assert.Equal(2, pending.Count);
        var first = pending[0];
        await f.Service.ReviewAsync(new ReviewSignalCommand(
            f.Repository.Id, Domain.Common.Id<CommercialSignal>.From(first.SignalId),
            first.Version, "Reject", null, null, "owner", DateTimeOffset.UtcNow), CancellationToken.None);
        Assert.Single(await f.Service.ListAsync(f.Repository.Id, "reviewed", CancellationToken.None));
        Assert.Single(await f.Service.ListAsync(f.Repository.Id, "pending", CancellationToken.None));
    }
}

internal sealed record SignalFixture(
    CommercialSignalService Service,
    InMemoryRepositoryStore Repositories,
    StubSourceProvider Sources,
    Repository Repository)
{
    public static SignalFixture CreateService(
        ISignalClassifier? classifier = null,
        ICommercialSourceProvider? sourceProvider = null)
    {
        var signals = new InMemoryCommercialSignalStore();
        var reviews = new InMemorySignalReviewStore();
        var repos = new InMemoryRepositoryStore(new InMemoryMembershipStore());
        var sources = sourceProvider as StubSourceProvider;
        ICommercialSourceProvider provider = sourceProvider ?? (sources = new StubSourceProvider());
        var service = new CommercialSignalService(
            signals, reviews, provider, classifier ?? new KeywordSignalClassifier(), repos);
        var repo = new Repository("N_TEST", "owner", "repo", RepositoryVisibility.Public, DateTimeOffset.UtcNow);
        repos.Add(repo);
        return new SignalFixture(service, repos, sources ?? new StubSourceProvider(), repo);
    }
}
