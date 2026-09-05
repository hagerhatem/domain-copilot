using DomainCopilot.Application.Agents.SafetyChecker;

namespace DomainCopilot.Application.Agents.DocumentationDrafter;

/// <summary>
/// A structured clinical note draft. This is the agent's "write" tool output
/// (AgentTool.DraftClinicalNote) and, per the project brief, must never be finalized
/// without passing the Clinician approval gate. That gating is enforced by the
/// orchestrator via DomainCopilot.Domain.Entities.AgentRun.RequestApproval /
/// ApplyApprovalDecision - this record is only the draft content itself, produced
/// before that gate is ever reached, regardless of what any ingested document content
/// claims about prior approval (see case_syn-015).
///
/// Kept structured (not a single free-text blob) so FR-2's mandatory citation
/// traceability survives into the final note: CitedChunkIds lets a reviewer - or an
/// automated check - verify every claim in AssessmentAndPlan traces back to an actual
/// retrieved chunk, the same property the evaluation harness's groundedness metric
/// checks for at the agent-output level.
/// </summary>
public sealed record ClinicalNoteDraft
{
    public string Subjective { get; }
    public string AssessmentAndPlan { get; }
    public IReadOnlyList<Guid> CitedChunkIds { get; }
    public IReadOnlyList<SafetyWarning> AddressedSafetyWarnings { get; }

    /// <summary>
    /// True if any AddressedSafetyWarnings entry has Severity ==
    /// SafetyWarningSeverity.Contraindicated. A hint for the orchestrator/UI to
    /// surface prominently at the approval gate - it does not change the gating
    /// behavior itself, since every draft passes through the same gate regardless.
    /// </summary>
    public bool RequiresClinicianAttention { get; }

    public ClinicalNoteDraft(
        string subjective,
        string assessmentAndPlan,
        IReadOnlyList<Guid> citedChunkIds,
        IReadOnlyList<SafetyWarning> addressedSafetyWarnings)
    {
        if (string.IsNullOrWhiteSpace(subjective))
            throw new ArgumentException("Subjective section cannot be empty.", nameof(subjective));
        if (string.IsNullOrWhiteSpace(assessmentAndPlan))
            throw new ArgumentException("Assessment and plan section cannot be empty.", nameof(assessmentAndPlan));
        ArgumentNullException.ThrowIfNull(citedChunkIds);
        ArgumentNullException.ThrowIfNull(addressedSafetyWarnings);

        Subjective = subjective.Trim();
        AssessmentAndPlan = assessmentAndPlan.Trim();
        CitedChunkIds = citedChunkIds;
        AddressedSafetyWarnings = addressedSafetyWarnings;
        RequiresClinicianAttention = addressedSafetyWarnings.Any(w => w.Severity == SafetyWarningSeverity.Contraindicated);
    }
}

public sealed record DocumentationDrafterOutput
{
    public ClinicalNoteDraft Draft { get; }

    public DocumentationDrafterOutput(ClinicalNoteDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        Draft = draft;
    }
}
