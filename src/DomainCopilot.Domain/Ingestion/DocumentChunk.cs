namespace DomainCopilot.Domain.Ingestion;

public sealed record ChunkId(Guid Value)
{
    public static ChunkId New() => new(Guid.NewGuid());
}

public sealed class DocumentChunk
{
    public ChunkId Id { get; private set; } = null!;
    public DocumentId DocumentId { get; private set; } = null!;
    public int ChunkIndex { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public string? Section { get; private set; }
    public int? PageNumber { get; private set; }
    public int DocumentVersion { get; private set; }

    private DocumentChunk() { }

    public static DocumentChunk Create(
        DocumentId documentId,
        int chunkIndex,
        string text,
        string? section,
        int? pageNumber,
        int documentVersion) => new()
        {
            Id = ChunkId.New(),
            DocumentId = documentId,
            ChunkIndex = chunkIndex,
            Text = text,
            Section = section,
            PageNumber = pageNumber,
            DocumentVersion = documentVersion
        };
}
