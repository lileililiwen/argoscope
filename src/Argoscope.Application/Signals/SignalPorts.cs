using Argoscope.Domain.Signals;

namespace Argoscope.Application.Signals;

/// <summary>Classifier outcome. Malformed output is reported as failure, never as a category.</summary>
public sealed record SignalClassification(
    SignalCategory Category,
    double Confidence,
    string ClassifierVersion,
    string Rationale);

/// <summary>Classifier contract. Implementations MUST NOT mutate GitHub, scores or lifecycle state.</summary>
public interface ISignalClassifier
{
    string ClassifierVersion { get; }

    Task<ClassifierResult> ClassifyAsync(string excerpt, CancellationToken cancellationToken);
}

/// <summary>Success carries a validated classification; failure carries a retryable diagnostic.</summary>
public sealed record ClassifierResult(
    bool IsSuccess,
    SignalClassification? Classification,
    string? DiagnosticCode);
