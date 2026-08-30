namespace DomainCopilot.Application.Ingestion.Ports;

using DomainCopilot.Domain.Common;
using DomainCopilot.Domain.Ingestion;

public sealed record VectorRecord(
    ChunkId ChunkId,
    DocumentId DocumentId,
    int DocumentVersion,
    float[] Vector,
    string Text,
    string? Section,
    int? PageNumber,
    string? GuidelineVersionLabel,
    DateTimeOffset? GuidelineEffectiveDate);

// --- Added for FR-2 hybrid retrieval (dense leg) ---

public sealed record VectorSearchFilter(
    string? GuidelineVersionLabel = null,
    DateTimeOffset? EffectiveOnOrAfter = null,
    DateTimeOffset? EffectiveOnOrBefore = null,
    DocumentId? DocumentId = null);

public sealed record VectorSearchQuery(float[] Vector, int TopK, VectorSearchFilter? Filter = null);

public sealed record VectorSearchHit(
    ChunkId ChunkId,
    DocumentId DocumentId,
    int DocumentVersion,
    string Text,
    string? Section,
    int? PageNumber,
    string? GuidelineVersionLabel,
    DateTimeOffset? GuidelineEffectiveDate,
    float Score); // cosine similarity, Qdrant native scale, higher = more similar

public interface IVectorStore
{
    Task<Result<Unit>> UpsertAsync(IReadOnlyList<VectorRecord> records, CancellationToken ct);

    // Called before re-indexing a new version, so stale chunks never surface in retrieval.
    Task<Result<Unit>> DeleteByDocumentIdAsync(DocumentId documentId, CancellationToken ct);

    // Added for FR-2 hybrid retrieval (dense leg).
    Task<Result<IReadOnlyList<VectorSearchHit>>> SearchAsync(VectorSearchQuery query, CancellationToken ct);
}
