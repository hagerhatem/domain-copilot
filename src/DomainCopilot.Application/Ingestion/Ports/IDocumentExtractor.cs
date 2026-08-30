namespace DomainCopilot.Application.Ingestion.Ports;

using DomainCopilot.Domain.Common;
using DomainCopilot.Domain.Ingestion;

public interface IDocumentExtractor
{
    DocumentFormat Format { get; }

    Task<Result<ExtractedDocument>> ExtractAsync(DocumentSource source, CancellationToken ct);
}
