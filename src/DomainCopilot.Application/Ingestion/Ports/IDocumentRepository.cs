namespace DomainCopilot.Application.Ingestion.Ports;

using DomainCopilot.Domain.Ingestion;

public interface IDocumentRepository
{
    Task<Document?> GetBySourceKeyAsync(string sourceKey, CancellationToken ct);

    Task AddAsync(Document document, CancellationToken ct);

    Task UpdateAsync(Document document, CancellationToken ct);

    // Overwrites the relational metadata rows for this document's chunks
    // (used on both first ingest and every re-ingest).
    Task ReplaceChunksAsync(DocumentId documentId, IReadOnlyList<DocumentChunk> chunks, CancellationToken ct);

    // Added for FR-2 hybrid retrieval: a single batch lookup of parent Document
    // metadata (filename, guideline version label/date) for the distinct set of
    // documents a fused top-K result set spans, so citations don't cost an N+1
    // round-trip per chunk. Documents that no longer exist are simply omitted from
    // the result rather than causing a failure — the caller treats a missing id as
    // "skip this chunk" (see RrfHybridRetrievalService).

    Task<IReadOnlyList<Document>> GetManyByIdsAsync(IReadOnlyList<DocumentId> ids, CancellationToken ct);

    /// <summary>Prompt 12.5.2: minimal listing for the Ingest screen's "previously ingested documents" table. Ordered most-recently-updated first.</summary>
    Task<IReadOnlyList<Document>> GetAllAsync(CancellationToken ct);
}
