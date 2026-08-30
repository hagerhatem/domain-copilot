namespace DomainCopilot.Application.Retrieval.Ports;

using DomainCopilot.Domain.Common;
using DomainCopilot.Domain.Ingestion;

public sealed record KeywordSearchFilter(
    string? GuidelineVersionLabel = null,
    DateTimeOffset? EffectiveOnOrAfter = null,
    DateTimeOffset? EffectiveOnOrBefore = null,
    DocumentId? DocumentId = null);

public sealed record KeywordSearchQuery(string QueryText, int TopK, KeywordSearchFilter? Filter = null);

public sealed record KeywordSearchHit(
    ChunkId ChunkId,
    DocumentId DocumentId,
    int DocumentVersion,
    string Text,
    string? Section,
    int? PageNumber,
    double Rank); // SQL Server FREETEXTTABLE RANK column; higher = more relevant, unbounded scale

/// <summary>
/// Keyword/lexical search over chunk text. SQL-Server-specific today (FREETEXTTABLE),
/// but kept behind a port so the hybrid retrieval service never depends on that detail —
/// e.g. a future Postgres migration would ship one new adapter using tsvector, nothing
/// else changes (Section 3 acceptance test).
/// </summary>
public interface IKeywordSearchService
{
    Task<Result<IReadOnlyList<KeywordSearchHit>>> SearchAsync(KeywordSearchQuery query, CancellationToken ct);
}
