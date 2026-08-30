using DomainCopilot.Evaluation.Contracts;

namespace DomainCopilot.Evaluation.Metrics;

public sealed record RetrievalHitRateResult(
    bool AnyExpectedDocumentHit,
    bool AllExpectedDocumentsHit,
    IReadOnlyList<string> MissedDocuments);

public static class RetrievalHitRateCalculator
{
    /// <summary>
    /// Hit-rate for a single question. "Any" hit is the primary FR-3 metric
    /// (did at least one correct source appear in top-K); "all" is a stricter
    /// secondary stat worth tracking separately for multi-document questions
    /// like the cross-guideline and comorbidity cases, where citing only one
    /// of the two relevant guidelines is a real (if partial) miss.
    /// </summary>
    public static RetrievalHitRateResult Evaluate(
        IReadOnlyList<string> expectedSourceDocuments,
        IReadOnlyList<RetrievedChunk> retrievedChunks)
    {
        var retrievedDocs = retrievedChunks
            .Select(c => c.SourceDocument)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missed = expectedSourceDocuments
            .Where(expected => !retrievedDocs.Contains(expected))
            .ToList();

        return new RetrievalHitRateResult(
            AnyExpectedDocumentHit: missed.Count < expectedSourceDocuments.Count,
            AllExpectedDocumentsHit: missed.Count == 0,
            MissedDocuments: missed);
    }
}
