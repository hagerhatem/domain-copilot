namespace DomainCopilot.Application.Retrieval;

using DomainCopilot.Application.Retrieval.Ports;
using DomainCopilot.Domain.Errors;

/// <summary>
/// Outcome of an evidence-sufficiency check. Deliberately a plain result value rather
/// than an exception or a DomainCopilot.Domain.Common.Result&lt;T&gt; — that Result&lt;T&gt;
/// wraps DomainCopilot.Domain.Common.DomainError (a record), whereas
/// InsufficientEvidenceError is DomainCopilot.Domain.Errors.DomainError (an Exception
/// subtype); the two are unrelated types on purpose (see AgentRun/TokenBudget), and
/// this class stays consistent with the Errors.DomainError family used everywhere else
/// error handling touches an aggregate like AgentRun.
/// </summary>
public sealed class EvidenceSufficiencyResult
{
    public bool IsSufficient { get; }

    /// <summary>Populated only when IsSufficient is true — the chunks that cleared the threshold.</summary>
    public IReadOnlyList<RetrievedChunk> SupportingChunks { get; }

    /// <summary>Populated only when IsSufficient is false, ready to throw or propagate as-is.</summary>
    public InsufficientEvidenceError? RefusalError { get; }

    private EvidenceSufficiencyResult(
        bool isSufficient,
        IReadOnlyList<RetrievedChunk> supportingChunks,
        InsufficientEvidenceError? refusalError)
    {
        IsSufficient = isSufficient;
        SupportingChunks = supportingChunks;
        RefusalError = refusalError;
    }

    public static EvidenceSufficiencyResult Sufficient(IReadOnlyList<RetrievedChunk> supportingChunks) =>
        new(true, supportingChunks, null);

    public static EvidenceSufficiencyResult Insufficient(InsufficientEvidenceError error) =>
        new(false, Array.Empty<RetrievedChunk>(), error);
}

/// <summary>
/// FR-2's mandatory refusal check: decides, from fused hybrid-retrieval results alone,
/// whether there is enough grounded evidence to let an LLM attempt an answer at all.
///
/// This is the central risk-mitigation control for Domain Pack D0 (Healthcare) named
/// in the project brief: confident hallucination of dosage or contraindication
/// information is the failure this exists to prevent. Refusing here is a correct,
/// required outcome — not a fallback path to special-case away.
///
/// The future AskQuestionUseCase is expected to call this immediately after hybrid
/// retrieval and before any ILLMProvider call: if IsSufficient is false, it should
/// throw/propagate RefusalError instead of calling the LLM — consistent with how
/// TokenBudget.Consume and AgentRun.ApplyApprovalDecision already throw
/// DomainCopilot.Domain.Errors.DomainError subtypes elsewhere in this codebase, and
/// with how AgentRun.Refuse(InsufficientEvidenceError) already accepts one as a plain
/// value.
///
/// Scope note: this class only ever produces NoRelevantChunks or LowConfidence as the
/// InsufficientEvidenceReason. ConflictingSources (retrieved chunks disagree with each
/// other) and OutOfCorpus (the question concerns something never ingested at all)
/// need semantic judgement a purely score-based check cannot make on its own — those
/// are expected to be raised elsewhere (e.g. the Safety Checker agent cross-checking
/// contradictory guidance, or a separate corpus-scope classifier), not here.
/// </summary>
public sealed class EvidenceSufficiencyChecker
{
    private readonly EvidenceSufficiencyOptions _options;

    public EvidenceSufficiencyChecker(EvidenceSufficiencyOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public EvidenceSufficiencyResult Check(string query, IReadOnlyList<RetrievedChunk> retrievedChunks)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be empty.", nameof(query));
        ArgumentNullException.ThrowIfNull(retrievedChunks);

        // Nothing came back from retrieval at all — distinct from "retrieval found
        // something, but it wasn't good enough" (LowConfidence, below). This split
        // matters for FR-3's evaluation harness and for giving the clinician a more
        // precise refusal message than a single generic "no" ever could.
        if (retrievedChunks.Count == 0)
        {
            return EvidenceSufficiencyResult.Insufficient(
                new InsufficientEvidenceError(query, InsufficientEvidenceReason.NoRelevantChunks));
        }

        var supportingChunks = retrievedChunks
            .Where(c => c.FusedScore >= _options.MinFusedScore)
            .ToList();

        if (supportingChunks.Count < _options.MinSupportingChunks)
        {
            return EvidenceSufficiencyResult.Insufficient(
                new InsufficientEvidenceError(query, InsufficientEvidenceReason.LowConfidence));
        }

        return EvidenceSufficiencyResult.Sufficient(supportingChunks);
    }
}
