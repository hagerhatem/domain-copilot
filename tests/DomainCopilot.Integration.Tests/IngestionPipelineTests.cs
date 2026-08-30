namespace DomainCopilot.Integration.Tests;

using DomainCopilot.Application.Common;
using DomainCopilot.Application.Ingestion;
using DomainCopilot.Application.Ingestion.Ports;
using DomainCopilot.Domain.Ingestion;
using DomainCopilot.Infrastructure.Ingestion.Chunking;
using DomainCopilot.Infrastructure.Ingestion.Cleaning;
using DomainCopilot.Infrastructure.Ingestion.Extraction;
using DomainCopilot.Infrastructure.Ingestion.Persistence;
using DomainCopilot.Infrastructure.Ingestion.VectorStore;
using DomainCopilot.Integration.Tests.Fixtures;
using DomainCopilot.Integration.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Xunit;

[Collection(IngestionContainersCollection.Name)]
public sealed class IngestionPipelineTests : IAsyncLifetime
{
    // -----------------------------------------------------------------------
    // The PDF is the real seed file: who_hypertension-guideline_2021-08_v1.pdf
    // (copied into the test output as SeedData/sample-guideline.pdf via the .csproj).
    //
    // ONLY THIS ONE LINE STILL NEEDS YOUR INPUT: open that PDF, copy one full
    // sentence verbatim (ideally from a recommendation or diagnosis section —
    // something distinctive, not a page header/footer), and paste it below.
    // -----------------------------------------------------------------------
    private const string SeedPdfRelativePath = "SeedData/sample-guideline.pdf";
    private const string KnownPhraseFromDocument =
        "WHO recommends initiation of pharmacological antihypertensive treatment of individuals " +
        "with a confirmed diagnosis of hypertension and systolic blood pressure of \u2265140 mmHg " +
        "or diastolic blood pressure of \u226590 mmHg.";

    private readonly IngestionContainersFixture _containers;
    private readonly string _collectionName = $"test_{Guid.NewGuid():N}";

    private DomainCopilotDbContext _db = null!;
    private QdrantClient _qdrantClient = null!;
    private IngestDocumentUseCase _useCase = null!;
    private FakeDeterministicEmbeddingService _embeddingService = null!;

    public IngestionPipelineTests(IngestionContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        var dbOptions = new DbContextOptionsBuilder<DomainCopilotDbContext>()
            .UseSqlServer(_containers.SqlConnectionString)
            .Options;
        _db = new DomainCopilotDbContext(dbOptions);
        await _db.Database.EnsureCreatedAsync();

        _qdrantClient = new QdrantClient(_containers.QdrantHost, _containers.QdrantGrpcPort);

        _embeddingService = new FakeDeterministicEmbeddingService(dimensions: 256);

        var qdrantOptions = Options.Create(new QdrantOptions
        {
            CollectionName = _collectionName,
            VectorSize = _embeddingService.Dimensions
        });

        var chunkingOptions = Options.Create(new ChunkingOptions());

        var repository = new EfDocumentRepository(_db);
        var vectorStore = new QdrantVectorStore(_qdrantClient, qdrantOptions);
        var extractors = new IDocumentExtractor[] { new PdfPigDocumentExtractor(), new OpenXmlDocxExtractor() };
        var cleaner = new PlainTextDocumentCleaner();
        var chunker = new ClinicalSectionAwareChunkingStrategy(chunkingOptions);
        var clock = new FixedClock(DateTimeOffset.UtcNow);

        _useCase = new IngestDocumentUseCase(
            extractors, cleaner, chunker, _embeddingService, vectorStore, repository, clock);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _qdrantClient.Dispose();
    }

    [Fact]
    public async Task Ingesting_a_real_pdf_creates_chunks_with_correct_metadata()
    {
        var source = LoadSeedPdf() with
        {
            SourceKeyOverride = $"who-hypertension-{Guid.NewGuid():N}",
            GuidelineVersionLabel = "v2021-08"
        };
        var command = new IngestDocumentCommand(source);

        var result = await _useCase.ExecuteAsync(command, CancellationToken.None);

        Assert.True(result.Outcome == IngestionOutcome.Created, $"Expected Created but got {result.Outcome}. Stage: {result.FailedAtStage}. Reason: {result.FailureReason}");
        Assert.True(result.ChunkCount > 0, "Expected at least one chunk to be produced from the seed PDF.");
        Assert.Null(result.FailureReason);

        // --- (a) chunks created with correct metadata, in SQL Server ------------
        var chunks = await _db.DocumentChunks
            .Where(c => c.DocumentId == result.DocumentId)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync();

        Assert.Equal(result.ChunkCount, chunks.Count);
        Assert.All(chunks, c => Assert.False(string.IsNullOrWhiteSpace(c.Text)));
        Assert.All(chunks, c => Assert.Equal(1, c.DocumentVersion)); // first ingest => version 1
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.ChunkIndex));

        var document = await _db.Documents.SingleAsync(d => d.Id == result.DocumentId);
        Assert.Equal("v2021-08", document.GuidelineVersionLabel);
        Assert.Equal(DocumentStatus.Completed, document.Status);

        // --- (a continued) same metadata correctly landed in Qdrant's payload ----
        var pointIds = chunks.Select(c => new PointId { Uuid = c.Id.Value.ToString() }).ToList();
        var points = await _qdrantClient.RetrieveAsync(_collectionName, pointIds, withPayload: true, withVectors: false);

        Assert.Equal(chunks.Count, points.Count);
        foreach (var point in points)
        {
            Assert.Equal(result.DocumentId.Value.ToString(), point.Payload["document_id"].StringValue);
            Assert.Equal("v2021-08", point.Payload["guideline_version_label"].StringValue);
        }
    }

    [Fact]
    public async Task Re_ingesting_the_same_file_does_not_create_duplicates()
    {
        var source = LoadSeedPdf() with { SourceKeyOverride = $"who-hypertension-{Guid.NewGuid():N}" };
        var command = new IngestDocumentCommand(source);

        var firstResult = await _useCase.ExecuteAsync(command, CancellationToken.None);
        Assert.True(firstResult.Outcome == IngestionOutcome.Created, $"Expected Created but got {firstResult.Outcome}. Stage: {firstResult.FailedAtStage}. Reason: {firstResult.FailureReason}");

        var chunkCountAfterFirstIngest = await _db.DocumentChunks
            .CountAsync(c => c.DocumentId == firstResult.DocumentId);

        var pointCountAfterFirstIngest = await CountQdrantPointsForDocumentAsync(firstResult.DocumentId);

        // --- (b) re-ingest the identical bytes -----------------------------------
        var secondResult = await _useCase.ExecuteAsync(command, CancellationToken.None);

        Assert.Equal(IngestionOutcome.Skipped, secondResult.Outcome);
        Assert.Equal(firstResult.DocumentId, secondResult.DocumentId);
        Assert.Equal(firstResult.Version, secondResult.Version);

        // Keyed only on this test's unique SourceKeyOverride — other tests share the
        // same FileName (same physical seed PDF), so matching on FileName too would
        // over-count rows created by sibling tests in the same shared SQL container.
        var documentCount = await _db.Documents.CountAsync(d => d.SourceKey == source.SourceKeyOverride);
        Assert.Equal(1, documentCount); // no duplicate Document row

        var chunkCountAfterSecondIngest = await _db.DocumentChunks
            .CountAsync(c => c.DocumentId == firstResult.DocumentId);
        Assert.Equal(chunkCountAfterFirstIngest, chunkCountAfterSecondIngest);

        var pointCountAfterSecondIngest = await CountQdrantPointsForDocumentAsync(firstResult.DocumentId);
        Assert.Equal(pointCountAfterFirstIngest, pointCountAfterSecondIngest);
    }

    [Fact]
    public async Task Querying_the_vector_store_for_a_known_phrase_returns_a_relevant_chunk()
    {
        Assert.False(
            KnownPhraseFromDocument.StartsWith("REPLACE_WITH", StringComparison.Ordinal),
            "Set KnownPhraseFromDocument to real text from the WHO hypertension PDF before running this test.");

        var source = LoadSeedPdf() with { SourceKeyOverride = $"who-hypertension-{Guid.NewGuid():N}" };
        var command = new IngestDocumentCommand(source);
        var ingestResult = await _useCase.ExecuteAsync(command, CancellationToken.None);
        Assert.True(ingestResult.Outcome == IngestionOutcome.Created, $"Expected Created but got {ingestResult.Outcome}. Stage: {ingestResult.FailedAtStage}. Reason: {ingestResult.FailureReason}");

        // --- (c) query the vector store directly (no retrieval port exists yet —
        // see the note above IVectorStore about this being a Phase 5/FR-2 concern) ---
        var queryVector = _embeddingService.Embed(KnownPhraseFromDocument);

        var searchResults = await _qdrantClient.SearchAsync(
            _collectionName,
            queryVector,
            limit: 3);

        Assert.NotEmpty(searchResults);

        var topHit = searchResults[0];
        Assert.Equal(ingestResult.DocumentId.Value.ToString(), topHit.Payload["document_id"].StringValue);

        var topHitText = topHit.Payload["text"].StringValue;
        var overlapsWithQuery = KnownPhraseFromDocument
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(word => topHitText.Contains(word, StringComparison.OrdinalIgnoreCase));

        Assert.True(overlapsWithQuery,
            $"Expected the top hit's stored text to share at least one word with the query phrase. Top hit text: {topHitText}");
    }

    private async Task<ulong> CountQdrantPointsForDocumentAsync(DocumentId documentId)
    {
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

        return await _qdrantClient.CountAsync(_collectionName, filter);
    }

    private static DocumentSource LoadSeedPdf()
    {
        var path = Path.Combine(AppContext.BaseDirectory, SeedPdfRelativePath);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Seed PDF not found at '{path}'. Check the <None Include=... Link=.../> " +
                "entry in the .csproj points at a real file.", path);

        var bytes = File.ReadAllBytes(path);
        return new DocumentSource(Path.GetFileName(path), DocumentFormat.Pdf, bytes);
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;
        public DateTimeOffset UtcNow { get; }
    }
}
