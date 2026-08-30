namespace DomainCopilot.Application.Ingestion;

using DomainCopilot.Application.Ingestion.Ports;

public sealed record IngestDocumentCommand(DocumentSource Source);
