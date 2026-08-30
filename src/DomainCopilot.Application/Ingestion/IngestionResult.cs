namespace DomainCopilot.Application.Ingestion;

using DomainCopilot.Domain.Ingestion;

public enum IngestionOutcome
{
    Created,
    Updated,
    Skipped,
    Failed
}

public sealed record IngestionResult(
    DocumentId DocumentId,
    string FileName,
    string SourceKey,
    int Version,
    IngestionOutcome Outcome,
    int ChunkCount,
    string ContentHash,
    string? FailureReason,
    DocumentStatus? FailedAtStage,
    TimeSpan Duration)
{
    public bool IsSuccess => Outcome is IngestionOutcome.Created or IngestionOutcome.Updated or IngestionOutcome.Skipped;
}
