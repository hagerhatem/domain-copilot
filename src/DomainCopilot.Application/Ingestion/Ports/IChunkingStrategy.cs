namespace DomainCopilot.Application.Ingestion.Ports;

using DomainCopilot.Domain.Common;

public sealed record ChunkDraft(int ChunkIndex, string Text, string? Section, int? PageNumber);

public interface IChunkingStrategy
{
    // Concrete implementation follows whatever strategy ADR-001 justifies
    // (e.g. section-aware chunking with overlap for clinical guidelines).
    Result<IReadOnlyList<ChunkDraft>> Chunk(CleanedDocument document);
}
