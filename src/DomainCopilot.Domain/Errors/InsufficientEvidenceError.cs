namespace DomainCopilot.Domain.Errors;

/// <summary>
/// Raised when the system does not have enough grounded evidence to safely answer a
/// clinical question or draft part of a clinical note — for example, no matching
/// guideline chunks were retrieved, or the retrieved sources conflict with each other.
///
/// This is a CORRECT and REQUIRED outcome for Domain Pack D0 (Healthcare): the system
/// must refuse rather than let an agent infer a dosage or contraindication from
/// insufficient grounding. Application/Api code must surface this as an explicit,
/// clearly-labeled refusal — never silently retry with a lower evidence bar, and never
/// let an LLM "fill the gap" with an unconfirmed guess.
/// </summary>
public sealed class InsufficientEvidenceError : DomainError
{
    /// <summary>The question or sub-task that could not be safely answered.</summary>
    public string Query { get; }

    /// <summary>Why the evidence was judged insufficient.</summary>
    public InsufficientEvidenceReason Reason { get; }

    public InsufficientEvidenceError(string query, InsufficientEvidenceReason reason)
        : base("INSUFFICIENT_EVIDENCE", BuildMessage(query, reason))
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be empty.", nameof(query));

        Query = query;
        Reason = reason;
    }

    private static string BuildMessage(string query, InsufficientEvidenceReason reason) =>
        $"Insufficient evidence to answer '{query}' ({reason}).";
}

/// <summary>Why a query was refused for lack of grounded evidence.</summary>
public enum InsufficientEvidenceReason
{
    /// <summary>No corpus chunks met the relevance threshold.</summary>
    NoRelevantChunks,

    /// <summary>Retrieved chunks meaningfully disagree with one another.</summary>
    ConflictingSources,

    /// <summary>Retrieved chunks are only tangentially related; confidence too low to ground an answer.</summary>
    LowConfidence,

    /// <summary>The question falls outside the ingested corpus entirely.</summary>
    OutOfCorpus
}
