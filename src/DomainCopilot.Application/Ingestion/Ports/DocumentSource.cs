namespace DomainCopilot.Application.Ingestion.Ports;

using DomainCopilot.Domain.Ingestion;

public sealed record DocumentSource(
    string FileName,
    DocumentFormat Format,
    byte[] RawBytes,
    string? SourceKeyOverride = null,
    string? GuidelineVersionLabel = null,
    DateTimeOffset? GuidelineEffectiveDate = null);
