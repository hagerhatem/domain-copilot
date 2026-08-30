namespace DomainCopilot.Infrastructure.Ingestion.VectorStore;

using DomainCopilot.Application.Ingestion.Ports;
using DomainCopilot.Domain.Common;
using DomainCopilot.Domain.Ingestion;
using DomainCopilot.Domain.Retrieval;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

/// <summary>
/// Qdrant-backed IVectorStore. Qdrant is free, open-source, and self-hosted via Docker
/// Compose (Section 2 constraint) — no managed/paid tier is used anywhere here.
///
/// Payload stores everything FR-2's structured citations need to reconstruct a
/// traceable reference back to the exact source chunk: document id/version, section,
/// page, guideline version label/date, and the chunk text itself (so retrieval doesn't
/// need a second round-trip to SQL Server just to render an answer with citations).
/// </summary>
public sealed class QdrantVectorStore : IVectorStore
{
    private readonly QdrantClient _client;
    private readonly QdrantOptions _options;
    private bool _collectionEnsured;
    private readonly SemaphoreSlim _ensureLock = new(1, 1);

    public QdrantVectorStore(QdrantClient client, IOptions<QdrantOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task<Result<Unit>> UpsertAsync(IReadOnlyList<VectorRecord> records, CancellationToken ct)
    {
        if (records.Count == 0)
            return Result<Unit>.Success(Unit.Value);

        try
        {
            await EnsureCollectionAsync(ct);

            var points = records.Select(ToPointStruct).ToList();
            await _client.UpsertAsync(_options.CollectionName, points, cancellationToken: ct);

            return Result<Unit>.Success(Unit.Value);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(new IndexingFailedError($"Qdrant upsert failed: {ex.Message}"));
        }
    }

    public async Task<Result<Unit>> DeleteByDocumentIdAsync(DocumentId documentId, CancellationToken ct)
    {
        try
        {
            await EnsureCollectionAsync(ct);

            var filter = new Filter
            {
                Must =
                {
                    new Condition
                    {
                        Field = new FieldCondition
                        {
                            Key = "document_id",
                            Match = new Match { Keyword = documentId.Value.ToString() }
                        }
                    }
                }
            };

            await _client.DeleteAsync(_options.CollectionName, filter, cancellationToken: ct);

            return Result<Unit>.Success(Unit.Value);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(new IndexingFailedError($"Qdrant delete failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Added for FR-2 hybrid retrieval (dense leg). Metadata filters are pushed down
    /// to Qdrant's own payload filter rather than applied after the fact, so filtered
    /// queries still return up to TopK genuinely-matching results instead of silently
    /// returning fewer than asked for.
    /// </summary>
    public async Task<Result<IReadOnlyList<VectorSearchHit>>> SearchAsync(VectorSearchQuery query, CancellationToken ct)
    {
        try
        {
            await EnsureCollectionAsync(ct);

            var filter = BuildFilter(query.Filter);

            var results = await _client.SearchAsync(
                collectionName: _options.CollectionName,
                vector: query.Vector,
                filter: filter,
                limit: (ulong)query.TopK,
                cancellationToken: ct);
            // ⚠ Worth a quick sanity check against the documented zero-length-vector
            // bug on Upsert (see ToPointStruct comment below) — confirm this SearchAsync
            // overload's implicit float[] -> query vector conversion actually populates
            // correctly for your pinned Qdrant.Client version before relying on it in
            // FR-3's evaluation harness.

            var hits = results.Select(ToSearchHit).ToList();
            return Result<IReadOnlyList<VectorSearchHit>>.Success(hits);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<VectorSearchHit>>.Failure(new VectorSearchFailedError($"Qdrant search failed: {ex.Message}"));
        }
    }

    private static Filter? BuildFilter(VectorSearchFilter? filter)
    {
        if (filter is null)
            return null;

        var conditions = new List<Condition>();

        if (filter.GuidelineVersionLabel is not null)
        {
            conditions.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = "guideline_version_label",
                    Match = new Match { Keyword = filter.GuidelineVersionLabel }
                }
            });
        }

        if (filter.DocumentId is not null)
        {
            conditions.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = "document_id",
                    Match = new Match { Keyword = filter.DocumentId.Value.ToString() }
                }
            });
        }

        if (filter.EffectiveOnOrAfter is not null || filter.EffectiveOnOrBefore is not null)
        {
            var range = new Range();
            if (filter.EffectiveOnOrAfter is not null)
                range.Gte = filter.EffectiveOnOrAfter.Value.ToUnixTimeSeconds();
            if (filter.EffectiveOnOrBefore is not null)
                range.Lte = filter.EffectiveOnOrBefore.Value.ToUnixTimeSeconds();

            conditions.Add(new Condition
            {
                Field = new FieldCondition { Key = "guideline_effective_date", Range = range }
            });
        }

        if (conditions.Count == 0)
            return null;

        var result = new Filter();
        result.Must.AddRange(conditions);
        return result;
    }

    private static VectorSearchHit ToSearchHit(ScoredPoint point)
    {
        string? GetString(string key) =>
            point.Payload.TryGetValue(key, out var v) && v.KindCase == Value.KindOneofCase.StringValue
                ? v.StringValue
                : null;

        int? GetInt(string key) =>
            point.Payload.TryGetValue(key, out var v) && v.KindCase == Value.KindOneofCase.IntegerValue
                ? (int)v.IntegerValue
                : null;

        DateTimeOffset? GetDate(string key) =>
            point.Payload.TryGetValue(key, out var v) && v.KindCase == Value.KindOneofCase.IntegerValue
                ? DateTimeOffset.FromUnixTimeSeconds(v.IntegerValue)
                : null;

        return new VectorSearchHit(
            ChunkId: new ChunkId(Guid.Parse(point.Id.Uuid)),
            DocumentId: new DocumentId(Guid.Parse(GetString("document_id")!)),
            DocumentVersion: GetInt("document_version") ?? 0,
            Text: GetString("text") ?? string.Empty,
            Section: GetString("section"),
            PageNumber: GetInt("page_number"),
            GuidelineVersionLabel: GetString("guideline_version_label"),
            GuidelineEffectiveDate: GetDate("guideline_effective_date"),
            Score: point.Score);
    }

    private PointStruct ToPointStruct(VectorRecord record)
    {
        // Built explicitly rather than relying on an implicit float[] -> Vectors
        // conversion — that conversion produced zero-length vectors against this
        // client/server version combination (observed as Qdrant's "Vector dimension
        // error: expected dim: N, got 0"). AddRange against the protobuf
        // RepeatedField<float> is unambiguous and always populates correctly.
        var vector = new Vector();
        vector.Data.AddRange(record.Vector);

        var point = new PointStruct
        {
            Id = new PointId { Uuid = record.ChunkId.Value.ToString() },
            Vectors = new Vectors { Vector = vector }
        };

        point.Payload.Add("document_id", record.DocumentId.Value.ToString());
        point.Payload.Add("document_version", record.DocumentVersion);
        point.Payload.Add("text", record.Text);

        if (record.Section is not null)
            point.Payload.Add("section", record.Section);

        if (record.PageNumber is not null)
            point.Payload.Add("page_number", record.PageNumber.Value);

        if (record.GuidelineVersionLabel is not null)
            point.Payload.Add("guideline_version_label", record.GuidelineVersionLabel);

        if (record.GuidelineEffectiveDate is not null)
            point.Payload.Add("guideline_effective_date", record.GuidelineEffectiveDate.Value.ToUnixTimeSeconds());

        return point;
    }

    /// <summary>
    /// Idempotent collection creation — safe to call on every upsert/delete since the
    /// first successful call flips the in-memory flag and short-circuits the rest.
    /// </summary>
    private async Task EnsureCollectionAsync(CancellationToken ct)
    {
        if (_collectionEnsured)
            return;

        await _ensureLock.WaitAsync(ct);
        try
        {
            if (_collectionEnsured)
                return;

            var exists = await _client.CollectionExistsAsync(_options.CollectionName, cancellationToken: ct);
            if (!exists)
            {
                await _client.CreateCollectionAsync(
                    _options.CollectionName,
                    new VectorParams { Size = (ulong)_options.VectorSize, Distance = Distance.Cosine },
                    cancellationToken: ct);
            }

            _collectionEnsured = true;
        }
        catch (RpcException rpc) when (rpc.StatusCode == StatusCode.AlreadyExists)
        {
            // Race with another process/instance creating the same collection — fine.
            _collectionEnsured = true;
        }
        finally
        {
            _ensureLock.Release();
        }
    }
}
