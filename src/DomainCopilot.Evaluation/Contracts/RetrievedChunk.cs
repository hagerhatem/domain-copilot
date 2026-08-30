namespace DomainCopilot.Evaluation.Contracts;

/// <summary>
/// A single retrieved chunk, as it would come back from the real hybrid
/// retrieval pipeline (Qdrant dense + SQL Server full-text keyword fusion).
/// SourceDocument should be the same identifier scheme used in the golden
/// set's expected_source_documents list (currently: bare filename).
/// </summary>
public sealed record RetrievedChunk(
    string SourceDocument,
    string ChunkId,
    string Text,
    double Score);
