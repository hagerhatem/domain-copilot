namespace DomainCopilot.Infrastructure.Ingestion.Chunking;

using System.Text.RegularExpressions;
using DomainCopilot.Application.Ingestion.Ports;
using DomainCopilot.Domain.Common;
using DomainCopilot.Domain.Ingestion;
using Microsoft.Extensions.Options;

/// <summary>
/// Chunking strategy per ADR-001: clinical guideline/label documents (SmPC, USPI-style,
/// internal protocols) are almost always organized under a small, recurring set of
/// section headers ("Contraindications", "Dosage and Administration", "Drug
/// Interactions", etc.), often numbered ("4.3 Contraindications"). Splitting on those
/// headers keeps each chunk topically coherent, which matters directly for FR-2's
/// citation traceability and for the Safety Checker agent needing whole, uncut
/// contraindication sections rather than an arbitrary mid-sentence cut.
///
/// When a document has zero recognizable headers (e.g. a free-text case note or a
/// guideline authored without standard headings), the strategy falls back to plain
/// fixed-size overlapping chunks so ingestion never fails outright for lacking structure.
///
/// Any logical section — recognized or the fallback whole-document one — that still
/// exceeds MaxChunkChars is itself split into overlapping windows, so no single chunk
/// grows unbounded (which would blow up embedding cost and retrieval precision).
/// </summary>
public sealed class ClinicalSectionAwareChunkingStrategy : IChunkingStrategy
{
    private static readonly string[] KnownSectionHeaders =
    {
        "Indications and Usage", "Indications",
        "Contraindications",
        "Warnings and Precautions", "Special Warnings and Precautions for Use", "Warnings", "Precautions",
        "Adverse Reactions", "Undesirable Effects",
        "Drug Interactions", "Interactions with Other Medicinal Products and Other Forms of Interaction",
        "Dosage and Administration", "Posology and Method of Administration", "Dosage",
        "Overdosage", "Overdose",
        "Clinical Pharmacology", "Pharmacodynamic Properties", "Pharmacokinetic Properties", "Pharmacokinetics",
        "Mechanism of Action",
        "Use in Specific Populations", "Fertility, Pregnancy and Lactation", "Pregnancy", "Lactation",
        "Pediatric Use", "Geriatric Use",
        "Boxed Warning",
        "Description",
        "How Supplied", "Storage and Handling",
        "Patient Counseling Information",
        "Preclinical Safety Data", "Nonclinical Toxicology",
        "Clinical Trials", "Clinical Studies",
        "References"
    };

    // Matches an optional leading numeric outline prefix ("4.3", "4.3.", "4.3)")
    // followed by one of the known headers, alone on its line, optionally with a colon.
    private static readonly Regex HeaderPattern = new(
        $@"^\s*(?:\d+(?:\.\d+)*\.?\)?\s+)?(?<title>{string.Join("|", KnownSectionHeaders.Select(Regex.Escape))})\s*:?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ChunkingOptions _options;

    public ClinicalSectionAwareChunkingStrategy(IOptions<ChunkingOptions> options)
    {
        _options = options.Value;
    }

    public Result<IReadOnlyList<ChunkDraft>> Chunk(CleanedDocument document)
    {
        try
        {
            var logicalSections = BuildLogicalSections(document);

            var drafts = new List<ChunkDraft>();
            var index = 0;

            foreach (var section in logicalSections)
            {
                foreach (var windowText in SplitWithOverlap(section.Text, _options.MaxChunkChars, _options.OverlapChars, _options.MinChunkChars))
                {
                    drafts.Add(new ChunkDraft(index++, windowText, section.Title, section.PageNumber));
                }
            }

            return Result<IReadOnlyList<ChunkDraft>>.Success(drafts);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<ChunkDraft>>.Failure(new ChunkingFailedError(ex.Message));
        }
    }

    private sealed record LogicalSection(string? Title, int? PageNumber, string Text);

    private static List<LogicalSection> BuildLogicalSections(CleanedDocument document)
    {
        var sections = new List<LogicalSection>();
        string? currentTitle = null;
        int? currentPage = null;
        var buffer = new List<string>();
        var anyHeaderMatched = false;

        void Flush()
        {
            var text = string.Join('\n', buffer).Trim();
            if (!string.IsNullOrWhiteSpace(text))
                sections.Add(new LogicalSection(currentTitle, currentPage, text));
            buffer.Clear();
        }

        foreach (var cleanedSection in document.Sections)
        {
            foreach (var line in cleanedSection.Text.Split('\n'))
            {
                var match = HeaderPattern.Match(line);
                if (match.Success)
                {
                    anyHeaderMatched = true;
                    Flush();
                    currentTitle = NormalizeTitle(match.Groups["title"].Value);
                    currentPage = cleanedSection.PageNumber;
                }
                else
                {
                    // Track the page of the first content line in a section that has
                    // no explicit header yet (the pre-first-header preamble).
                    currentPage ??= cleanedSection.PageNumber;
                    buffer.Add(line);
                }
            }
        }

        Flush();

        if (!anyHeaderMatched)
        {
            // Fallback: no recognizable clinical headers anywhere in the document —
            // collapse to a single logical section spanning the whole document so the
            // fixed-size overlap splitter below is the only thing that runs.
            return new List<LogicalSection> { new(Title: null, PageNumber: document.Sections.FirstOrDefault()?.PageNumber, document.FullText) };
        }

        return sections;
    }

    private static string NormalizeTitle(string rawTitle) =>
        KnownSectionHeaders.FirstOrDefault(h => string.Equals(h, rawTitle, StringComparison.OrdinalIgnoreCase))
        ?? rawTitle.Trim();

    /// <summary>
    /// Word-boundary-aware sliding window. Never splits mid-word; the last window is
    /// merged into the previous one if it would otherwise fall below minChunkChars.
    /// </summary>
    private static IEnumerable<string> SplitWithOverlap(string text, int maxChunkChars, int overlapChars, int minChunkChars)
    {
        text = text.Trim();
        if (text.Length == 0)
            yield break;

        if (text.Length <= maxChunkChars)
        {
            yield return text;
            yield break;
        }

        var windows = new List<string>();
        var start = 0;

        while (start < text.Length)
        {
            var length = Math.Min(maxChunkChars, text.Length - start);
            var end = start + length;

            if (end < text.Length)
            {
                var lastSpace = text.LastIndexOf(' ', end - 1, Math.Min(length, 200));
                if (lastSpace > start)
                    end = lastSpace;
            }

            windows.Add(text[start..end].Trim());

            if (end >= text.Length)
                break;

            start = Math.Max(end - overlapChars, start + 1);
        }

        if (windows.Count > 1 && windows[^1].Length < minChunkChars)
        {
            var last = windows[^1];
            windows.RemoveAt(windows.Count - 1);
            windows[^1] = (windows[^1] + " " + last).Trim();
        }

        foreach (var window in windows)
            yield return window;
    }
}
