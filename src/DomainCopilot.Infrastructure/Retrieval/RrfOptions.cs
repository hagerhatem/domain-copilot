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
}
