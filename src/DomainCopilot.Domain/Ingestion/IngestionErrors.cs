namespace DomainCopilot.Domain.Ingestion;

using DomainCopilot.Domain.Common;

public sealed record UnsupportedFormatError(string Format)
    : DomainError("ingestion.unsupported_format", $"No extractor registered for format '{Format}'.");

public sealed record ExtractionFailedError(string Reason)
    : DomainError("ingestion.extraction_failed", Reason);

public sealed record EmptyDocumentError()
    : DomainError("ingestion.empty_document", "Document produced no extractable text after cleaning.");

public sealed record ChunkingFailedError(string Reason)
    : DomainError("ingestion.chunking_failed", Reason);

public sealed record EmbeddingFailedError(string Reason)
    : DomainError("ingestion.embedding_failed", Reason);

public sealed record IndexingFailedError(string Reason)
    : DomainError("ingestion.indexing_failed", Reason);
