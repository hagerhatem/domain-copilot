namespace DomainCopilot.Application.Ingestion.Ports;

using DomainCopilot.Domain.Common;

public interface IEmbeddingService
{
    int Dimensions { get; }

    Task<Result<IReadOnlyList<float[]>>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct);
}
