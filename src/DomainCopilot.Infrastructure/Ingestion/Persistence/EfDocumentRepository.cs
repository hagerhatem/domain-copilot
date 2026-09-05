namespace DomainCopilot.Infrastructure.Ingestion.Persistence;

using DomainCopilot.Application.Ingestion.Ports;
using DomainCopilot.Domain.Ingestion;
using Microsoft.EntityFrameworkCore;

public sealed class EfDocumentRepository : IDocumentRepository
{
    private readonly DomainCopilotDbContext _db;

    public EfDocumentRepository(DomainCopilotDbContext db)
    {
        _db = db;
    }

    public async Task<Document?> GetBySourceKeyAsync(string sourceKey, CancellationToken ct) =>
        await _db.Documents.SingleOrDefaultAsync(d => d.SourceKey == sourceKey, ct);

    public async Task AddAsync(Document document, CancellationToken ct)
    {
        _db.Documents.Add(document);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Document document, CancellationToken ct)
    {
        // Document is already tracked if it came from GetBySourceKeyAsync in the same
        // unit of work; Update() is a safe no-op duplicate-attach guard for the case
        // where it isn't (EF Core marks all properties modified either way, which is
        // fine here since the use case always mutates the full aggregate state).
        if (_db.Entry(document).State == EntityState.Detached)
            _db.Documents.Update(document);

        await _db.SaveChangesAsync(ct);
    }

    public async Task ReplaceChunksAsync(DocumentId documentId, IReadOnlyList<DocumentChunk> chunks, CancellationToken ct)
    {
        var existing = await _db.DocumentChunks
            .Where(c => c.DocumentId == documentId)
            .ToListAsync(ct);

        if (existing.Count > 0)
            _db.DocumentChunks.RemoveRange(existing);

        await _db.DocumentChunks.AddRangeAsync(chunks, ct);
        await _db.SaveChangesAsync(ct);
    }

    // Added for FR-2 hybrid retrieval: batch-loads parent Document metadata for a
    // fused top-K result set's distinct document ids, in one round-trip. Ids with no
    // matching row are silently absent from the result (see IDocumentRepository's
    // XML doc — the caller treats a missing id as "skip this chunk", not an error).
    //
    // AsNoTracking(): this is a read-only lookup for rendering citations, never
    // mutated or handed back to UpdateAsync — no reason to pay EF's change-tracking
    // cost for it.
    public async Task<IReadOnlyList<Document>> GetManyByIdsAsync(IReadOnlyList<DocumentId> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
            return Array.Empty<Document>();

        return await _db.Documents
            .AsNoTracking()
            .Where(d => ids.Contains(d.Id))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Document>> GetAllAsync(CancellationToken ct) =>
    await _db.Documents
        .AsNoTracking()
        .OrderByDescending(d => d.UpdatedAtUtc)
        .ToListAsync(ct);
}
