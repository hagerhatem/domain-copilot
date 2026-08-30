namespace DomainCopilot.Infrastructure.Ingestion.Chunking;

public sealed class ChunkingOptions
{
    public const string SectionName = "Chunking";

    /// <summary>Target maximum characters per chunk.</summary>
    public int MaxChunkChars { get; set; } = 1200;

    /// <summary>Character overlap between consecutive fixed-size chunks.</summary>
    public int OverlapChars { get; set; } = 150;

    /// <summary>
    /// Trailing fragments shorter than this are merged into the previous chunk
    /// instead of being emitted as their own tiny, low-signal chunk.
    /// </summary>
    public int MinChunkChars { get; set; } = 200;
}
