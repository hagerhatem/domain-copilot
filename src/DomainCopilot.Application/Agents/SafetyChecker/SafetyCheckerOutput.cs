namespace DomainCopilot.Application.Agents.SafetyChecker;

public enum SafetyWarningSeverity
{
    /// <summary>Worth noting, no action required.</summary>
    Info,
    /// <summary>Flag risk and recommend monitoring or dose review - not an absolute contraindication.</summary>
    Caution,
    /// <summary>Combination/condition is contraindicated outright.</summary>
    Contraindicated
}

/// <summary>
/// Where a warning came from. This is the field that makes "never an LLM guess"
/// (project brief, Section 4) auditable: a warning with Source ==
/// DeterministicLookup must have come from actual code-based rule evaluation, never
/// from an LLM completion, however confident-sounding.
/// </summary>
public enum SafetyWarningSource
{
    /// <summary>Produced by a deterministic, code-based interaction/contraindication rule engine - never an LLM guess.</summary>
    DeterministicLookup,
    /// <summary>Drawn from a retrieved guideline chunk (via this agent's SearchCorpus tool), not the deterministic lookup.</summary>
    RetrievedGuideline
}

/// <summary>
/// A single interaction/contraindication finding.
///
/// If a DeterministicLookup-sourced warning and a RetrievedGuideline-sourced warning
/// disagree about the same medication pair, that is exactly the "conflicting
/// sources" scenario DomainCopilot.Application.Retrieval.EvidenceSufficiencyChecker's
/// XML docs anticipate this agent surfacing (InsufficientEvidenceReason.ConflictingSources)
/// - this record doesn't resolve that conflict itself, it just makes both warnings
/// independently inspectable via Source so the orchestrator/implementation can detect
/// the disagreement and choose to refuse rather than silently pick one.
/// </summary>
public sealed record SafetyWarning
{
    public string Description { get; }
    public SafetyWarningSeverity Severity { get; }

    /// <summary>0.0-1.0. For DeterministicLookup findings this should typically be 1.0 (it's a rule match, not a guess).</summary>
    public double Confidence { get; }

    public SafetyWarningSource Source { get; }

    /// <summary>E.g. the deterministic rule id, or the ChunkId (as a string) of the guideline chunk this warning was drawn from.</summary>
    public string? SourceReferenceId { get; }

    public SafetyWarning(
        string description,
        SafetyWarningSeverity severity,
        double confidence,
        SafetyWarningSource source,
        string? sourceReferenceId = null)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Warning description cannot be empty.", nameof(description));
        if (confidence is < 0.0 or > 1.0)
            throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be between 0.0 and 1.0.");

        Description = description.Trim();
        Severity = severity;
        Confidence = confidence;
        Source = source;
        SourceReferenceId = sourceReferenceId?.Trim();
    }
}

/// <summary>An empty Warnings list is a valid, meaningful result: it means no interaction/contraindication was found, not that the check was skipped.</summary>
public sealed record SafetyCheckerOutput
{
    public IReadOnlyList<SafetyWarning> Warnings { get; }

    public SafetyCheckerOutput(IReadOnlyList<SafetyWarning> warnings)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        Warnings = warnings;
    }
}
