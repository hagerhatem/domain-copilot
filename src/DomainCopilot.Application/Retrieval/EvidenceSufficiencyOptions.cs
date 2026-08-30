namespace DomainCopilot.Application.Retrieval;

/// <summary>
/// Plain POCO, deliberately not wrapped in <c>IOptions&lt;T&gt;</c> here — this type lives
/// in the Application layer, which Section 3 restricts to depending only on Domain.
/// Composition-root code (Api/Infrastructure) is expected to bind this from
/// configuration via <c>services.Configure&lt;EvidenceSufficiencyOptions&gt;(...)</c> and then
/// register the unwrapped <c>.Value</c> for <see cref="EvidenceSufficiencyChecker"/> to
/// consume directly, so IOptions itself never has to appear in an Application-layer
/// constructor signature.
/// </summary>
public sealed class EvidenceSufficiencyOptions
{
    public const string SectionName = "Retrieval:EvidenceSufficiency";

    /// <summary>
    /// Must match whatever k the hybrid retrieval's RRF fusion actually used
    /// (see DomainCopilot.Infrastructure.Retrieval.RrfOptions.K, default 60) — this
    /// value only makes sense relative to that k, since RRF scores are meaningless
    /// on their own scale.
    /// </summary>
    public int RrfK { get; set; } = 60;

    /// <summary>
    /// A chunk counts as supporting evidence only if it ranked at or above this
    /// position in at least one of the two retrieval legs (dense or keyword) alone.
    /// Chosen so the threshold stays interpretable — "must be at least this
    /// well-ranked by one search leg" — rather than an opaque magic score nobody
    /// could defend in EVALUATION.md.
    /// </summary>
    public int RankCutoff { get; set; } = 10;

    /// <summary>
    /// The fused RRF score a chunk needs to count as supporting evidence, derived
    /// from RrfK and RankCutoff rather than set independently: 1 / (RrfK + RankCutoff)
    /// is exactly the score a chunk would get from ranking at RankCutoff in a single
    /// list and not appearing in the other at all.
    ///
    /// PROVISIONAL, like RrfOptions.K itself: this has not been tuned against the
    /// FR-3 golden set yet. EVALUATION.md must report the real hit-rate and refusal-
    /// correctness numbers this threshold produces, and this value should be revised
    /// honestly if they're poor — not left undocumented as if it were validated.
    /// </summary>
    public double MinFusedScore => 1.0 / (RrfK + RankCutoff);

    /// <summary>
    /// At least this many distinct chunks must individually clear MinFusedScore. A
    /// single borderline chunk is not enough to ground a clinical answer — FR-2's
    /// refusal requirement exists precisely so one weakly-relevant chunk can't
    /// produce a confident-sounding answer on its own.
    /// </summary>
    public int MinSupportingChunks { get; set; } = 2;
}
