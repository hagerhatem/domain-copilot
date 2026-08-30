namespace DomainCopilot.Domain.Ingestion;

public enum DocumentStatus
{
    Pending,
    Extracting,
    Cleaning,
    Chunking,
    Embedding,
    Indexing,
    Completed,
    Failed
}
