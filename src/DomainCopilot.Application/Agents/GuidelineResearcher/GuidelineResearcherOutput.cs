namespace DomainCopilot.Application.Agents.GuidelineResearcher;

/// <summary>
/// A single retrieved, citable excerpt of guideline content.
///
/// NOTE ON DocumentId/ChunkId: this repo currently has two parallel, non-identical
/// Document/DocumentChunk models - DomainCopilot.Domain.Entities.Document (Entity-based,
/// DocumentIngestionStatus) and DomainCopilot.Domain.Ingestion.Document
/// (DocumentId/ChunkId records, ContentHash-based idempotent re-ingestion,
/// GuidelineVersionLabel/EffectiveDate). This contract deliberately uses plain Guid
/// values rather than either model's identifier type so the agent contracts don't
/// have to take a side before that duplication is resolved. Whichever model is kept,
/// the future GuidelineResearcherAgent implementation is responsible for mapping its
/// real chunk identifiers into these plain Guids - this record itself needs no change
/// either way. FLAG FOR RESOLUTION: decide which Document model is canonical and
/// delete the other before wiring real ingestion/retrieval to these agents.
/// </summary>
public sealed record GuidelineExcerpt
{
    public Guid DocumentId { get; }
    public Guid ChunkId { get; }
    public string DocumentTitle { get; }
    public string DocumentVersion { get; }
    public string ExcerptText { get; }
    public string? SectionTitle { get; }
    public int? PageNumber { get; }

    /// <summary>Retrieval relevance score (fused dense+keyword), for transparency/debugging - not a clinical confidence measure.</summary>
    public double RelevanceScore { get; }

    public GuidelineExcerpt(
        Guid documentId,
        Guid chunkId,
        string documentTitle,
        string documentVersion,
        string excerptText,
        double relevanceScore,
        string? sectionTitle = null,
        int? pageNumber = null)
    {
        if (documentId == Guid.Empty)
            throw new ArgumentException("DocumentId cannot be empty.", nameof(documentId));
        if (chunkId == Guid.Empty)
            throw new ArgumentException("ChunkId cannot be empty.", nameof(chunkId));
        if (string.IsNullOrWhiteSpace(documentTitle))
            throw new ArgumentException("Document title cannot be empty.", nameof(documentTitle));
        if (string.IsNullOrWhiteSpace(documentVersion))
            throw new ArgumentException("Document version cannot be empty.", nameof(documentVersion));
        if (string.IsNullOrWhiteSpace(excerptText))
            throw new ArgumentException("Excerpt text cannot be empty.", nameof(excerptText));
        if (pageNumber is < 1)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), "Page number must be positive when specified.");

        DocumentId = documentId;
        ChunkId = chunkId;
        DocumentTitle = documentTitle.Trim();
        DocumentVersion = documentVersion.Trim();
        ExcerptText = excerptText.Trim();
        RelevanceScore = relevanceScore;
        SectionTitle = sectionTitle?.Trim();
        PageNumber = pageNumber;
    }
}

/// <summary>
/// Output of the Guideline Researcher. An empty <see cref="Excerpts"/> list is a valid
/// result on its own - the caller (orchestrator, via
/// DomainCopilot.Application.Retrieval.EvidenceSufficiencyChecker) is what decides
/// whether zero or weak excerpts means the run should refuse
/// (InsufficientEvidenceError), not this agent. This agent's job is only to report
/// what it found and how confident retrieval was, honestly - including reporting
/// nothing.
/// </summary>
public sealed record GuidelineResearcherOutput
{
    public IReadOnlyList<GuidelineExcerpt> Excerpts { get; }

    public GuidelineResearcherOutput(IReadOnlyList<GuidelineExcerpt> excerpts)
    {
        ArgumentNullException.ThrowIfNull(excerpts);
        Excerpts = excerpts;
    }
}
