namespace DomainCopilot.Infrastructure.Ingestion.VectorStore;

public sealed class QdrantOptions
{
    public const string SectionName = "Qdrant";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 6334; // gRPC port (REST is 6333)
    public bool UseTls { get; set; } = false;
    public string? ApiKey { get; set; } // null for local Docker Compose Qdrant

    public string CollectionName { get; set; } = "clinical_guidelines";

    /// <summary>Must match IEmbeddingService.Dimensions for the active embedding model.</summary>
    public int VectorSize { get; set; } = 768;
}
