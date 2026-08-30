namespace DomainCopilot.Infrastructure.Retrieval;

using DomainCopilot.Application.Ingestion.Ports;
using DomainCopilot.Application.Retrieval.Ports;
using DomainCopilot.Domain.Common;
using DomainCopilot.Domain.Ingestion;
using DomainCopilot.Domain.Retrieval;
using Microsoft.Extensions.Options;

public sealed class RrfHybridRetrievalService : IHybridRetrievalService
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly IKeywordSearchService _keywordSearch;
    private readonly IDocumentRepository _documentRepository;
    private readonly RrfOptions _options;

    public RrfHybridRetrievalService(
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        IKeywordSearchService keywordSearch,
        IDocumentRepository documentRepository,
        IOptions<RrfOptions> options)
    {
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _keywordSearch = keywordSearch;
        _documentRepository = documentRepository;
        _options = options.Value;
    }

    public async Task<Result<IReadOnlyList<RetrievedChunk>>> RetrieveAsync(RetrievalQuery query, CancellationToken ct)
    {
        var candidatePoolSize = Math.Max(query.TopK * _options.CandidatePoolMultiplier, _options.MinCandidatePool);

        // 1. Embed the query with the same embedding service used at ingestion time —
        //    dense retrieval is meaningless if query and corpus embeddings come from
        //    different models/dimensions.
        var embedResult = await _embeddingService.EmbedBatchAsync(new[] { query.QueryText }, ct);
        if (!embedResult.IsSuccess)
            return Result<IReadOnlyList<RetrievedChunk>>.Failure(new QueryEmbeddingFailedError(embedResult.Error!.Message));

        var queryVector = embedResult.Value![0];

        // 2. Dense leg (Qdrant) and keyword leg (SQL full-text) run independently and
        //    in parallel — they share no state and either can fail without the other's
        //    result being wasted, so fan them out rather than awaiting sequentially.
        var vectorFilter = query.Filter is null
            ? null
            : new VectorSearchFilter(
                query.Filter.GuidelineVersionLabel,
                query.Filter.EffectiveOnOrAfter,
                query.Filter.EffectiveOnOrBefore,
                query.Filter.DocumentId);

        var keywordFilter = query.Filter is null
            ? null
            : new KeywordSearchFilter(
                query.Filter.GuidelineVersionLabel,
                query.Filter.EffectiveOnOrAfter,
                query.Filter.EffectiveOnOrBefore,
                query.Filter.DocumentId);

        var denseTask = _vectorStore.SearchAsync(
            new VectorSearchQuery(queryVector, candidatePoolSize, vectorFilter), ct);
        var keywordTask = _keywordSearch.SearchAsync(
            new KeywordSearchQuery(query.QueryText, candidatePoolSize, keywordFilter), ct);

        await Task.WhenAll(denseTask, keywordTask);

        var denseResult = await denseTask;
        var keywordResult = await keywordTask;

        if (!denseResult.IsSuccess)
            return Result<IReadOnlyList<RetrievedChunk>>.Failure(new VectorSearchFailedError(denseResult.Error!.Message));
        if (!keywordResult.IsSuccess)
            return Result<IReadOnlyList<RetrievedChunk>>.Failure(new KeywordSearchFailedError(keywordResult.Error!.Message));

        var denseHits = denseResult.Value!;
        var keywordHits = keywordResult.Value!;

        // 3. Fuse by rank position (RRF), not raw score — the two scales are incomparable.
        var denseRankedIds = denseHits.Select(h => h.ChunkId.Value).ToList();
        var keywordRankedIds = keywordHits.Select(h => h.ChunkId.Value).ToList();
        var fusedScores = ReciprocalRankFusion.Fuse(denseRankedIds, keywordRankedIds, _options.K);

        var denseRankById = denseRankedIds
            .Select((id, i) => (id, rank: i + 1))
            .ToDictionary(x => x.id, x => x.rank);
        var keywordRankById = keywordRankedIds
            .Select((id, i) => (id, rank: i + 1))
            .ToDictionary(x => x.id, x => x.rank);

        var denseById = denseHits.ToDictionary(h => h.ChunkId.Value);
        var keywordById = keywordHits.ToDictionary(h => h.ChunkId.Value);

        var topFused = fusedScores
            .OrderByDescending(kv => kv.Value)
            .Take(query.TopK)
            .ToList();

        if (topFused.Count == 0)
            return Result<IReadOnlyList<RetrievedChunk>>.Success(Array.Empty<RetrievedChunk>());

        // 4. Load source document metadata (filename, guideline label) for citations —
        //    one batch lookup, not N+1 per chunk.
        var distinctDocumentIds = topFused
            .Select(kv => denseById.TryGetValue(kv.Key, out var d) ? d.DocumentId : keywordById[kv.Key].DocumentId)
            .Distinct()
            .ToList();

        // IDocumentRepository.GetManyByIdsAsync returns a plain Task<IReadOnlyList<Document>>
        // (not Result<T>) — unlike the vector/keyword search ports, a missing document id
        // isn't a failure mode here, it's an expected "already deleted" case handled below
        // by simply skipping that chunk, so there's nothing for a Result wrapper to add.
        var documents = await _documentRepository.GetManyByIdsAsync(distinctDocumentIds, ct);
        var documentsById = documents.ToDictionary(d => d.Id);

        var results = new List<RetrievedChunk>(topFused.Count);
        foreach (var (chunkGuid, fusedScore) in topFused)
        {
            var hasDense = denseById.TryGetValue(chunkGuid, out var dense);
            var hasKeyword = keywordById.TryGetValue(chunkGuid, out var keyword);

            var documentId = hasDense ? dense!.DocumentId : keyword!.DocumentId;
            var text = hasDense ? dense!.Text : keyword!.Text;
            var section = hasDense ? dense!.Section : keyword!.Section;
            var pageNumber = hasDense ? dense!.PageNumber : keyword!.PageNumber;
            var documentVersion = hasDense ? dense!.DocumentVersion : keyword!.DocumentVersion;

            if (!documentsById.TryGetValue(documentId, out var document))
                continue; // stale chunk whose parent document vanished between search and lookup — skip rather than fail the whole request

            results.Add(new RetrievedChunk(
                ChunkId: new ChunkId(chunkGuid),
                DocumentId: documentId,
                SourceDocumentFileName: document.FileName,
                GuidelineVersionLabel: document.GuidelineVersionLabel,
                GuidelineEffectiveDate: document.GuidelineEffectiveDate,
                DocumentVersion: documentVersion,
                Section: section,
                PageNumber: pageNumber,
                Text: text,
                FusedScore: fusedScore,
                DenseRank: denseRankById.GetValueOrDefault(chunkGuid) is var dr and > 0 ? dr : null,
                KeywordRank: keywordRankById.GetValueOrDefault(chunkGuid) is var kr and > 0 ? kr : null));
        }

        return Result<IReadOnlyList<RetrievedChunk>>.Success(results);
    }
}
