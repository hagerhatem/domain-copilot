namespace DomainCopilot.Domain.Retrieval;

using DomainCopilot.Domain.Common;

// Named QueryEmbeddingFailedError (not EmbeddingFailedError) specifically to avoid
// colliding with DomainCopilot.Domain.Ingestion.EmbeddingFailedError, which is the
// distinct ingestion-time embedding failure -- this one is a query-time failure when
// embedding the user's retrieval question, a different call site with a different
// meaning even though both wrap the same kind of underlying provider error.
public sealed record QueryEmbeddingFailedError(string Reason)
    : DomainError("retrieval.query_embedding_failed", Reason);

public sealed record VectorSearchFailedError(string Reason)
    : DomainError("retrieval.vector_search_failed", Reason);

public sealed record KeywordSearchFailedError(string Reason)
    : DomainError("retrieval.keyword_search_failed", Reason);
