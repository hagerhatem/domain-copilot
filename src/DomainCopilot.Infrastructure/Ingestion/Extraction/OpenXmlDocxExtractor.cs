namespace DomainCopilot.Infrastructure.Ingestion.Extraction;

using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DomainCopilot.Application.Ingestion.Ports;
using DomainCopilot.Domain.Common;
using DomainCopilot.Domain.Ingestion;

/// <summary>
/// DOCX text extraction via the OpenXML SDK (MIT-licensed, Microsoft-maintained).
/// DOCX has no reliable page concept without a rendering engine, so ExtractedPage
/// list is intentionally empty — page numbers for DOCX-sourced chunks will be null
/// throughout the pipeline, which is an accepted, documented limitation.
/// </summary>
public sealed class OpenXmlDocxExtractor : IDocumentExtractor
{
    public DocumentFormat Format => DocumentFormat.Docx;

    public Task<Result<ExtractedDocument>> ExtractAsync(DocumentSource source, CancellationToken ct)
    {
        try
        {
            using var stream = new MemoryStream(source.RawBytes);
            using var wordDoc = WordprocessingDocument.Open(stream, isEditable: false);

            var body = wordDoc.MainDocumentPart?.Document?.Body
                ?? throw new InvalidOperationException("DOCX has no document body.");

            var sb = new StringBuilder();
            var metadata = new Dictionary<string, string>();

            foreach (var paragraph in body.Elements<Paragraph>())
            {
                ct.ThrowIfCancellationRequested();

                var text = paragraph.InnerText;
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                // Preserve heading style as a light hint for the chunker — clinical
                // authoring tools often tag section titles with a "Heading*" style.
                var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
                if (styleId is not null && styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
                    sb.Append("\n## ").Append(text).Append('\n');
                else
                    sb.Append(text).Append('\n');
            }

            var coreProps = wordDoc.PackageProperties;
            if (!string.IsNullOrWhiteSpace(coreProps.Title))
                metadata["title"] = coreProps.Title;
            if (!string.IsNullOrWhiteSpace(coreProps.Creator))
                metadata["author"] = coreProps.Creator;

            var extracted = new ExtractedDocument(sb.ToString(), Pages: Array.Empty<ExtractedPage>(), metadata);
            return Task.FromResult(Result<ExtractedDocument>.Success(extracted));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                Result<ExtractedDocument>.Failure(new ExtractionFailedError($"DOCX extraction failed: {ex.Message}")));
        }
    }
}
