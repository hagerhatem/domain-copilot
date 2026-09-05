using DomainCopilot.Application.Ingestion.Ports;
using DomainCopilot.Application.Llm;
using DomainCopilot.Domain.Common;
using DomainCopilot.Domain.Ingestion;
using DomainCopilot.Infrastructure.Llm;

namespace DomainCopilot.Infrastructure.Ingestion.Embedding;

/// <summary>
/// Real IEmbeddingService implementation, closing the foundational gap that left
/// IHybridRetrievalService (and therefore GuidelineResearcherAgent/SafetyCheckerAgent)
/// unable to resolve at runtime, and left IngestDocumentUseCase's Embedding stage
/// unimplemented. This is a thin wrapper - it deliberately does NOT duplicate any
/// HTTP/JSON logic, it delegates to OllamaLLMProvider.EmbedAsync (already fully
/// implemented for Phase 7's ILLMProvider work).
///
/// Uses its own internal ILLMProvider instance (a second OllamaLLMProvider,
/// constructed with the embedding-specific model) rather than the ILLMProvider
/// registered for completions, because embedding and completion need different
/// Ollama models (e.g. nomic-embed-text vs llama3) - one registered ILLMProvider
/// instance is pinned to one model, so this needed its own HttpClient/model pair
/// rather than reusing the completion provider's DI registration.
///
/// Dimensions must match DomainCopilot.Infrastructure.Ingestion.VectorStore.QdrantOptions.VectorSize
/// (currently 768, which is exactly nomic-embed-text's output size - no mismatch to
/// resolve for that specific model choice, but if the embedding model is ever
/// changed, VectorSize must be updated to match, or Qdrant will reject every insert).
/// </summary>
public sealed class OllamaEmbeddingService : IEmbeddingService
{
    private readonly ILLMProvider _embeddingProvider;

    public int Dimensions { get; }

    public OllamaEmbeddingService(HttpClient httpClient, string model = "nomic-embed-text", int dimensions = 768)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (dimensions <= 0)
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Dimensions must be positive.");

        // Reuses OllamaLLMProvider purely for its EmbedAsync implementation - this
        // instance's CompleteAsync/StreamAsync/CallTool are never called from here.
        _embeddingProvider = new OllamaLLMProvider(httpClient, model);
        Dimensions = dimensions;
    }

    public async Task<Result<IReadOnlyList<float[]>>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        if (texts is null || texts.Count == 0)
        {
            return Result<IReadOnlyList<float[]>>.Failure(
                new EmbeddingFailedError("EmbedBatchAsync called with an empty text list."));
        }

        try
        {
            var response = await _embeddingProvider.EmbedAsync(new EmbedRequest(texts), ct);

            var mismatched = response.Vectors.FirstOrDefault(v => v.Count != Dimensions);
            if (mismatched is not null)
            {
                return Result<IReadOnlyList<float[]>>.Failure(new EmbeddingFailedError(
                    $"Embedding model '{response.ModelUsed}' returned a {mismatched.Count}-dimension vector, " +
                    $"but this service is configured for {Dimensions} dimensions (must match QdrantOptions.VectorSize)."));
            }

            var vectors = response.Vectors.Select(v => v.ToArray()).ToList();
            return Result<IReadOnlyList<float[]>>.Success(vectors);
        }
        catch (Exception ex)
        {
            // Deliberately broad: any transport/HTTP/JSON failure from the local
            // Ollama call becomes a domain-level EmbeddingFailedError rather than an
            // unhandled exception, matching every other Result<T>-returning port in
            // this codebase (see IHybridRetrievalService, IKeywordSearchService).
            return Result<IReadOnlyList<float[]>>.Failure(new EmbeddingFailedError(ex.Message));
        }
    }
}
