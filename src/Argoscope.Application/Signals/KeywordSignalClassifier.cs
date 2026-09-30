using Argoscope.Domain.Signals;

namespace Argoscope.Application.Signals;

/// <summary>
/// Deterministic keyword classifier used as the default advisory model and
/// by tests. Malformed input is never produced here; the
/// <see cref="MalformedSignalClassifier"/> test double covers that path.
/// No network, no credentials, no side effects.
/// </summary>
public sealed class KeywordSignalClassifier : ISignalClassifier
{
    public const double LowConfidenceFloor = 0.65;

    public string ClassifierVersion => "keyword-1";

    public Task<ClassifierResult> ClassifyAsync(string excerpt, CancellationToken cancellationToken)
    {
        var text = (excerpt ?? "").ToLowerInvariant();
        SignalCategory category;
        double confidence;
        if (ContainsAny(text, "self-host", "self host", "on-prem", "on prem", "host it for us", "managed hosting", "hosted version", "hosting plan"))
        {
            category = SignalCategory.HostedRequest;
            confidence = 0.88;
        }
        else if (ContainsAny(text, "paid support", "support contract", "support plan", "sla ", "enterprise support"))
        {
            category = SignalCategory.PaidSupport;
            confidence = 0.86;
        }
        else if (ContainsAny(text, "sso", "saml", "audit log", "soc 2", "soc2", "rbac", "scim", "enterprise"))
        {
            category = SignalCategory.EnterpriseCapability;
            confidence = 0.82;
        }
        else if (ContainsAny(text, "procurement", "purchase order", "vendor review", "security questionnaire", "msa ", "dpa "))
        {
            category = SignalCategory.ProcurementQuestion;
            confidence = 0.84;
        }
        else if (ContainsAny(text, "bug", "stack trace", "repro", "feature request", "docs typo", "question:"))
        {
            category = SignalCategory.NotCommercial;
            confidence = 0.78;
        }
        else
        {
            category = SignalCategory.Unclear;
            confidence = 0.5;
        }
        if (confidence < LowConfidenceFloor)
        {
            category = SignalCategory.Unclear;
        }
        var rationale = $"Keyword match suggests {category} (confidence {confidence:0.00}) from the stored excerpt.";
        return Task.FromResult(new ClassifierResult(true, new SignalClassification(category, confidence, ClassifierVersion, rationale), null));
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        foreach (var n in needles)
        {
            if (text.Contains(n, StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
