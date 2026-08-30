namespace DomainCopilot.Application.Retrieval.Ports;

using DomainCopilot.Domain.Common;
using DomainCopilot.Domain.Ingestion;

public sealed record RetrievalFilter(
    string? GuidelineVersionLabel = null,
    DateTimeOffset? EffectiveOnOrAfter = null,
    DateTimeOffset? EffectiveOnOrBefore = null,
    DocumentId? DocumentId = null);

public sealed record RetrievalQuery(string QueryText, int TopK = 10, RetrievalFilter? Filter = null);

/// <summary>
/// A single retrieved chunk, carrying everything FR-2's mandatory structured
/// citations need to point back to the exact source location, plus per-source rank
/// diagnostics (nullable — a chunk found by only one leg has null for the other) so
/// the FR-3 evaluation harness and the UI trace view can show *why* something ranked
/// where it did, not just the final fused score.
/// </summary>
public sealed record RetrievedChunk(
    ChunkId ChunkId,
    DocumentId DocumentId,
    string SourceDocumentFileName,
    string? GuidelineVersionLabel,
    DateTimeOffset? GuidelineEffectiveDate,
    int DocumentVersion,
    string? Section,
    int? PageNumber,
    string Text,
    double FusedScore,
    int? DenseRank,
    int? KeywordRank);

public interface IHybridRetrievalService
{
    Task<Result<IReadOnlyList<RetrievedChunk>>> RetrieveAsync(RetrievalQuery query, CancellationToken ct);
}
