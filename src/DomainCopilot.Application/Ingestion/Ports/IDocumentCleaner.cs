namespace DomainCopilot.Application.Ingestion.Ports;

using DomainCopilot.Domain.Common;

public interface IDocumentCleaner
{
    // Deterministic normalization only — whitespace, de-hyphenation, boilerplate
    // header/footer stripping. No LLM involved anywhere in this stage.
    Task<Result<CleanedDocument>> CleanAsync(ExtractedDocument extracted, CancellationToken ct);
}
