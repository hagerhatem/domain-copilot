namespace DomainCopilot.Infrastructure.Retrieval;

public sealed class RrfOptions
{
    public const string SectionName = "Retrieval:Rrf";

    /// <summary>Standard RRF constant per Cormack et al. — see ReciprocalRankFusion for rationale.</summary>
    public int K { get; set; } = 60;

    /// <summary>Each source is asked for TopK * this many candidates before fusion.</summary>
    public int CandidatePoolMultiplier { get; set; } = 3;

    /// <summary>Floor on candidates per source regardless of the multiplier, for small TopK values.</summary>
    public int MinCandidatePool { get; set; } = 20;

    /// <summary>
    /// KNOWN LIMITATION (temporary, environment-specific - see
    /// RrfHybridRetrievalService's XML doc on where this is applied): a raw
    /// cosine-similarity floor on the dense (Qdrant) leg, applied before RRF
    /// fusion. Exists because rank-based fusion alone has no way to reject an
    /// irrelevant chunk when the corpus is too small for real competition between
    /// candidates - confirmed directly with an off-topic query against a two-
    /// document demo corpus. 0.5 is a rough, UNTUNED value (nomic-embed-text's
    /// cosine similarities for genuinely related text are typically well above
    /// this; unrelated text is typically well below it, but this has not been
    /// validated against the FR-3 golden set - same caveat as
    /// EvidenceSufficiencyOptions.MinFusedScore). Revisit once Full-Text Search is
    /// enabled (restoring the keyword leg's contribution) and a real-sized golden
    /// set exists to tune against.
    /// </summary>
    public float MinDenseSimilarity { get; set; } = 0.5f;
}