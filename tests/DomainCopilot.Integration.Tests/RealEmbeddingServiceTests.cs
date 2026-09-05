namespace DomainCopilot.Integration.Tests;

using DomainCopilot.Infrastructure.Ingestion.Embedding;
using Xunit;

/// <summary>
/// Validates OllamaEmbeddingService against a REAL local Ollama instance - the
/// missing check this codebase never had: that IEmbeddingService's production
/// implementation actually returns real, well-formed vectors, not just that the
/// pipeline plumbing works end-to-end with FakeDeterministicEmbeddingService
/// (IngestionPipelineTests.cs already covers that, deliberately, and should keep
/// using the fake - see that file's XML docs for why).
///
/// Tagged RequiresOllama so it can be excluded where a local Ollama instance isn't
/// available (e.g. a CI runner that hasn't installed/pulled it):
///   dotnet test --filter "Category!=RequiresOllama"
/// To run only this test:
///   dotnet test --filter "Category=RequiresOllama"
///
/// Requires: `ollama pull nomic-embed-text` already run once, and Ollama reachable
/// at http://localhost:11434 (or set OLLAMA_BASE_URL to override).
/// </summary>
[Trait("Category", "RequiresOllama")]
public sealed class RealEmbeddingServiceTests : IAsyncLifetime
{
    private HttpClient _httpClient = null!;
    private OllamaEmbeddingService _embeddingService = null!;

    public Task InitializeAsync()
    {
        var baseUrl = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL") ?? "http://localhost:11434";
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _embeddingService = new OllamaEmbeddingService(_httpClient);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _httpClient.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task EmbedBatchAsync_ReturnsSuccess_WithA768DimensionVector_ThatIsNotAllZeros()
    {
        var result = await _embeddingService.EmbedBatchAsync(
            new[] { "Metformin is contraindicated in severe renal impairment." },
            CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success but got failure: {result.Error?.Message}. " +
            "Is Ollama running (`ollama serve`) with nomic-embed-text pulled (`ollama pull nomic-embed-text`)?");

        var vector = Assert.Single(result.Value);
        Assert.Equal(768, vector.Length);
        Assert.Equal(768, _embeddingService.Dimensions);

        // A real embedding is never literally all zeros - a stub/placeholder that
        // silently returned an empty/zeroed array would pass a naive "is it 768
        // long" check but fail this one, which is exactly the failure mode this
        // test exists to catch.
        Assert.Contains(vector, v => v != 0f);
    }

    [Fact]
    public async Task EmbedBatchAsync_ReturnsDifferentVectors_ForSemanticallyDifferentTexts()
    {
        var result = await _embeddingService.EmbedBatchAsync(
            new[] { "Metformin dosing in renal impairment.", "Weather forecast for tomorrow in Cairo." },
            CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success but got failure: {result.Error?.Message}");
        Assert.Equal(2, result.Value.Count);

        var vectorA = result.Value[0];
        var vectorB = result.Value[1];

        // Not a strict semantic-similarity assertion (that belongs to FR-3's
        // evaluation harness, not this smoke test) - just confirms the service
        // isn't returning the same constant/placeholder vector for every input.
        Assert.NotEqual(vectorA, vectorB);
    }

    [Fact]
    public async Task EmbedBatchAsync_ReturnsIdenticalVectors_ForTheSameTextEmbeddedTwice()
    {
        const string text = "ACE inhibitor plus potassium-sparing diuretic: hyperkalemia risk.";

        var firstResult = await _embeddingService.EmbedBatchAsync(new[] { text }, CancellationToken.None);
        var secondResult = await _embeddingService.EmbedBatchAsync(new[] { text }, CancellationToken.None);

        Assert.True(firstResult.IsSuccess);
        Assert.True(secondResult.IsSuccess);

        // Ollama's embedding models are deterministic for a fixed model+input - two
        // calls with identical text should produce identical (or at least
        // essentially identical) vectors. This guards against accidentally wiring
        // in a provider with sampling/temperature affecting embeddings, which would
        // silently break retrieval reproducibility.
        Assert.Equal(firstResult.Value[0], secondResult.Value[0]);
    }
}
