namespace DomainCopilot.Application.Ingestion.Ports;

public sealed record ExtractedPage(int PageNumber, string Text);

public sealed record ExtractedDocument(
    string RawText,
    IReadOnlyList<ExtractedPage> Pages, // empty for formats with no page concept
    IReadOnlyDictionary<string, string> ExtractedMetadata);
