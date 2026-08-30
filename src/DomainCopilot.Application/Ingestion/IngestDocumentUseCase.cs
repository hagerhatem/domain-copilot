namespace DomainCopilot.Application.Ingestion;

using System.Diagnostics;
using System.Security.Cryptography;
using DomainCopilot.Application.Common;
using DomainCopilot.Application.Ingestion.Ports;
using DomainCopilot.Domain.Ingestion;

public sealed class IngestDocumentUseCase
{
    private readonly IReadOnlyDictionary<DocumentFormat, IDocumentExtractor> _extractorsByFormat;
    private readonly IDocumentCleaner _cleaner;
    private readonly IChunkingStrategy _chunker;
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly IDocumentRepository _repository;
    private readonly IClock _clock;

    public IngestDocumentUseCase(
        IEnumerable<IDocumentExtractor> extractors,
        IDocumentCleaner cleaner,
        IChunkingStrategy chunker,
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        IDocumentRepository repository,
        IClock clock)
    {
        _extractorsByFormat = extractors.ToDictionary(e => e.Format);
        _cleaner = cleaner;
        _chunker = chunker;
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _repository = repository;
        _clock = clock;
    }

    public async Task<IngestionResult> ExecuteAsync(IngestDocumentCommand command, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var source = command.Source;
        var sourceKey = source.SourceKeyOverride ?? NormalizeSourceKey(source.FileName);
        var contentHash = ComputeSha256(source.RawBytes);

        var existing = await _repository.GetBySourceKeyAsync(sourceKey, ct);

        // --- Idempotency short-circuit ------------------------------------------
        if (existing is not null && existing.ContentHash == contentHash && existing.Status == DocumentStatus.Completed)
        {
            stopwatch.Stop();
            return new IngestionResult(
                existing.Id, source.FileName, sourceKey, existing.Version,
                IngestionOutcome.Skipped, existing.ChunkCount, contentHash,
                FailureReason: null, FailedAtStage: null, stopwatch.Elapsed);
        }

        var isNewVersion = existing is not null;
        var document = existing ?? Document.CreateNew(
            sourceKey, source.FileName, source.Format, contentHash,
            source.GuidelineVersionLabel, source.GuidelineEffectiveDate, _clock.UtcNow);

        if (isNewVersion)
            document.StartNewVersion(contentHash, _clock.UtcNow);

        if (existing is null)
            await _repository.AddAsync(document, ct);

        try
        {
            // --- Extract ---------------------------------------------------------
            if (!_extractorsByFormat.TryGetValue(source.Format, out var extractor))
                return await FailAsync(document, DocumentStatus.Extracting,
                    new UnsupportedFormatError(source.Format.ToString()).Message, stopwatch, ct);

            document.MarkStage(DocumentStatus.Extracting, _clock.UtcNow);
            await _repository.UpdateAsync(document, ct);

            var extractResult = await extractor.ExtractAsync(source, ct);
            if (extractResult.IsFailure)
                return await FailAsync(document, DocumentStatus.Extracting, extractResult.Error!.Message, stopwatch, ct);

            // --- Clean -------------------------------------------------------------
            document.MarkStage(DocumentStatus.Cleaning, _clock.UtcNow);
            await _repository.UpdateAsync(document, ct);

            var cleanResult = await _cleaner.CleanAsync(extractResult.Value, ct);
            if (cleanResult.IsFailure)
                return await FailAsync(document, DocumentStatus.Cleaning, cleanResult.Error!.Message, stopwatch, ct);

            if (string.IsNullOrWhiteSpace(cleanResult.Value.FullText))
                return await FailAsync(document, DocumentStatus.Cleaning, new EmptyDocumentError().Message, stopwatch, ct);

            // --- Chunk ---------------------------------------------------------------
            document.MarkStage(DocumentStatus.Chunking, _clock.UtcNow);
            await _repository.UpdateAsync(document, ct);

            var chunkResult = _chunker.Chunk(cleanResult.Value);
            if (chunkResult.IsFailure)
                return await FailAsync(document, DocumentStatus.Chunking, chunkResult.Error!.Message, stopwatch, ct);

            var drafts = chunkResult.Value;
            if (drafts.Count == 0)
                return await FailAsync(document, DocumentStatus.Chunking, "Chunking produced zero chunks.", stopwatch, ct);

            // --- Embed -----------------------------------------------------------------
            document.MarkStage(DocumentStatus.Embedding, _clock.UtcNow);
            await _repository.UpdateAsync(document, ct);

            var embedResult = await _embeddingService.EmbedBatchAsync(drafts.Select(d => d.Text).ToList(), ct);
            if (embedResult.IsFailure)
                return await FailAsync(document, DocumentStatus.Embedding, embedResult.Error!.Message, stopwatch, ct);

            var vectors = embedResult.Value;
            if (vectors.Count != drafts.Count)
                return await FailAsync(document, DocumentStatus.Embedding,
                    $"Embedding count mismatch: {vectors.Count} vectors for {drafts.Count} chunks.", stopwatch, ct);

            var chunks = drafts
                .Select(d => DocumentChunk.Create(document.Id, d.ChunkIndex, d.Text, d.Section, d.PageNumber, document.Version))
                .ToList();

            // --- Index (vector store + relational metadata) ---------------------------
            document.MarkStage(DocumentStatus.Indexing, _clock.UtcNow);
            await _repository.UpdateAsync(document, ct);

            if (isNewVersion)
            {
                var deleteResult = await _vectorStore.DeleteByDocumentIdAsync(document.Id, ct);
                if (deleteResult.IsFailure)
                    return await FailAsync(document, DocumentStatus.Indexing, deleteResult.Error!.Message, stopwatch, ct);
            }

            var records = chunks.Zip(vectors, (chunk, vector) => new VectorRecord(
                    chunk.Id, document.Id, document.Version, vector, chunk.Text,
                    chunk.Section, chunk.PageNumber, document.GuidelineVersionLabel, document.GuidelineEffectiveDate))
                .ToList();

            var upsertResult = await _vectorStore.UpsertAsync(records, ct);
            if (upsertResult.IsFailure)
                return await FailAsync(document, DocumentStatus.Indexing, upsertResult.Error!.Message, stopwatch, ct);

            await _repository.ReplaceChunksAsync(document.Id, chunks, ct);

            document.MarkCompleted(chunks.Count, _clock.UtcNow);
            await _repository.UpdateAsync(document, ct);

            stopwatch.Stop();
            return new IngestionResult(
                document.Id, source.FileName, sourceKey, document.Version,
                isNewVersion ? IngestionOutcome.Updated : IngestionOutcome.Created,
                chunks.Count, contentHash, FailureReason: null, FailedAtStage: null, stopwatch.Elapsed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Unexpected adapter failure (network blip, unhandled parser exception, etc.)
            // — never let one bad document take down a batch ingest run.
            return await FailAsync(document, document.Status, ex.Message, stopwatch, ct);
        }
    }

    private async Task<IngestionResult> FailAsync(
        Document document, DocumentStatus failedStage, string reason, Stopwatch stopwatch, CancellationToken ct)
    {
        document.MarkFailed(reason, _clock.UtcNow);
        await _repository.UpdateAsync(document, ct);
        stopwatch.Stop();

        return new IngestionResult(
            document.Id, document.FileName, document.SourceKey, document.Version,
            IngestionOutcome.Failed, ChunkCount: 0, document.ContentHash,
            FailureReason: reason, FailedAtStage: failedStage, stopwatch.Elapsed);
    }

    private static string NormalizeSourceKey(string fileName) => fileName.Trim().ToLowerInvariant();

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
