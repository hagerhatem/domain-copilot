using DomainCopilot.Domain.Common;

namespace DomainCopilot.Domain.Entities;

/// <summary>
/// The Clinician's auditable decision on a drafted clinical note: approve, reject, or
/// edit-and-approve. An <see cref="ApprovalDecision"/> is immutable once created — it
/// is a historical record and is never mutated after the fact (see also
/// <see cref="AgentRun.ApplyApprovalDecision"/>, which guarantees at most one decision
/// is ever attached to a run).
///
/// Invariants:
/// - <see cref="RejectionReason"/> is populated if and only if <see cref="DecisionType"/>
///   is <see cref="ApprovalDecisionType.Reject"/>; enforced by only exposing dedicated
///   factory methods (<see cref="Approve"/>, <see cref="Reject"/>,
///   <see cref="EditAndApprove"/>) rather than a single constructor with optional
///   parameters that could be mismatched.
/// - <see cref="EditedContent"/> is populated if and only if <see cref="DecisionType"/>
///   is <see cref="ApprovalDecisionType.EditAndApprove"/>.
/// - <see cref="OriginalDraftContent"/> is always captured, even on plain approval, so
///   the audit trail always shows exactly what the Documentation Drafter produced
///   versus what (if anything) the Clinician changed.
/// </summary>
public sealed class ApprovalDecision : Entity
{
    public Guid RunId { get; private set; }
    public Guid ClinicianUserId { get; private set; }
    public ApprovalDecisionType DecisionType { get; private set; }
    public string OriginalDraftContent { get; private set; } = string.Empty;
    public string? EditedContent { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTimeOffset DecidedAt { get; private set; }

    private ApprovalDecision()
    {
    }

    private ApprovalDecision(
        Guid id,
        Guid runId,
        Guid clinicianUserId,
        ApprovalDecisionType decisionType,
        string originalDraftContent,
        string? editedContent,
        string? rejectionReason,
        DateTimeOffset decidedAt) : base(id)
    {
        RunId = runId;
        ClinicianUserId = clinicianUserId;
        DecisionType = decisionType;
        OriginalDraftContent = originalDraftContent;
        EditedContent = editedContent;
        RejectionReason = rejectionReason;
        DecidedAt = decidedAt;
    }

    public static ApprovalDecision Approve(
        Guid runId, Guid clinicianUserId, string originalDraftContent, DateTimeOffset? decidedAt = null) =>
        CreateInternal(runId, clinicianUserId, ApprovalDecisionType.Approve, originalDraftContent,
            editedContent: null, rejectionReason: null, decidedAt);

    public static ApprovalDecision Reject(
        Guid runId, Guid clinicianUserId, string originalDraftContent, string rejectionReason, DateTimeOffset? decidedAt = null)
    {
        if (string.IsNullOrWhiteSpace(rejectionReason))
            throw new ArgumentException("A rejection reason is required.", nameof(rejectionReason));

        return CreateInternal(runId, clinicianUserId, ApprovalDecisionType.Reject, originalDraftContent,
            editedContent: null, rejectionReason.Trim(), decidedAt);
    }

    public static ApprovalDecision EditAndApprove(
        Guid runId, Guid clinicianUserId, string originalDraftContent, string editedContent, DateTimeOffset? decidedAt = null)
    {
        if (string.IsNullOrWhiteSpace(editedContent))
            throw new ArgumentException("Edited content is required for an edit-and-approve decision.", nameof(editedContent));

        return CreateInternal(runId, clinicianUserId, ApprovalDecisionType.EditAndApprove, originalDraftContent,
            editedContent.Trim(), rejectionReason: null, decidedAt);
    }

    private static ApprovalDecision CreateInternal(
        Guid runId,
        Guid clinicianUserId,
        ApprovalDecisionType decisionType,
        string originalDraftContent,
        string? editedContent,
        string? rejectionReason,
        DateTimeOffset? decidedAt)
    {
        if (runId == Guid.Empty)
            throw new ArgumentException("RunId cannot be empty.", nameof(runId));
        if (clinicianUserId == Guid.Empty)
            throw new ArgumentException("ClinicianUserId cannot be empty.", nameof(clinicianUserId));
        if (string.IsNullOrWhiteSpace(originalDraftContent))
            throw new ArgumentException("Original draft content cannot be empty.", nameof(originalDraftContent));

        return new ApprovalDecision(
            Guid.NewGuid(),
            runId,
            clinicianUserId,
            decisionType,
            originalDraftContent.Trim(),
            editedContent,
            rejectionReason,
            decidedAt ?? DateTimeOffset.UtcNow);
    }
}
