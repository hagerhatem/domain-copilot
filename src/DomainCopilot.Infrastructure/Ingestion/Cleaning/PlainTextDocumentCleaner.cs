namespace DomainCopilot.Infrastructure.Ingestion.Cleaning;

using DomainCopilot.Application.Ingestion.Ports;
using DomainCopilot.Domain.Common;

/// <summary>
/// STAND-IN ONLY. This is not the deterministic cleaner Section 7 requires (boilerplate
/// header/footer stripping, de-hyphenation, etc.) — it exists purely so the DI graph for
/// Prompt 4.2 compiles end-to-end. Replace with a real IDocumentCleaner implementation
/// when that piece of the ingestion pipeline is specced.
/// </summary>
public sealed class PlainTextDocumentCleaner : IDocumentCleaner
{
    public Task<Result<CleanedDocument>> CleanAsync(ExtractedDocument extracted, CancellationToken ct)
    {
        var sections = extracted.Pages.Count > 0
            ? extracted.Pages
                .Select(p => new CleanedSection(SectionTitle: null, p.PageNumber, NormalizeWhitespace(p.Text)))
                .Where(s => !string.IsNullOrWhiteSpace(s.Text))
                .ToList()
            : new List<CleanedSection> { new(SectionTitle: null, PageNumber: null, NormalizeWhitespace(extracted.RawText)) };

        return Task.FromResult(Result<CleanedDocument>.Success(new CleanedDocument(sections)));
    }

    private static string NormalizeWhitespace(string text) =>
        string.Join('\n', text.Split('\n').Select(l => l.TrimEnd())).Trim();
}
