namespace DomainCopilot.Infrastructure.Ingestion.Extraction;

using DomainCopilot.Application.Ingestion.Ports;
using DomainCopilot.Domain.Common;
using DomainCopilot.Domain.Ingestion;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

/// <summary>
/// PDF text extraction via PdfPig (Apache 2.0 license — permissive, no AGPL-style
/// copyleft, safe for this project's "no paid / no restrictive license" constraint).
/// </summary>
public sealed class PdfPigDocumentExtractor : IDocumentExtractor
{
    public DocumentFormat Format => DocumentFormat.Pdf;

    public Task<Result<ExtractedDocument>> ExtractAsync(DocumentSource source, CancellationToken ct)
    {
        try
        {
            using var stream = new MemoryStream(source.RawBytes);
            using var document = PdfDocument.Open(stream);

            var pages = new List<ExtractedPage>(document.NumberOfPages);
            var metadata = new Dictionary<string, string>();

            foreach (var page in document.GetPages())
            {
                ct.ThrowIfCancellationRequested();

                // ContentOrderTextExtractor gives more reading-order-faithful text than
                // page.Text, which matters for clinical tables/columns.
                var text = ContentOrderTextExtractor.GetText(page);
                pages.Add(new ExtractedPage(page.Number, text));
            }

            if (document.Information is { } info)
            {
                if (!string.IsNullOrWhiteSpace(info.Title))
                    metadata["title"] = info.Title;
                if (!string.IsNullOrWhiteSpace(info.Author))
                    metadata["author"] = info.Author;
            }

            var rawText = string.Join("\n\n", pages.Select(p => p.Text));

            var extracted = new ExtractedDocument(rawText, pages, metadata);
            return Task.FromResult(Result<ExtractedDocument>.Success(extracted));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Corrupt/encrypted/malformed PDFs land here — surfaced as a per-document
            // failure by the use case rather than crashing a batch ingest run.
            return Task.FromResult(
                Result<ExtractedDocument>.Failure(new ExtractionFailedError($"PDF extraction failed: {ex.Message}")));
        }
    }
}
