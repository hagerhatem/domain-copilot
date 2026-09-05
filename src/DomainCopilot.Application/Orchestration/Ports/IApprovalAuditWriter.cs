namespace DomainCopilot.Application.Orchestration.Ports;

/// <summary>
/// One append-only audit record for a Clinician's approve/reject/edit-approve
/// decision. Deliberately separate from DomainCopilot.Domain.Entities.ApprovalDecision:
/// ApprovalDecision is the business record AgentRun's state machine consumes
/// (AgentRun.ApplyApprovalDecision), while this is a dedicated, append-only audit
/// trail entry per Prompt 8.2 ("who, when, previous state, decision, comment") -
/// PreviousStatus specifically is audit-trail-only information ApprovalDecision has
/// no need to carry for its own business purpose.
/// </summary>
public sealed record ApprovalAuditEntry(
    Guid RunId,
    Guid ClinicianUserId,
    string PreviousStatus,
    string DecisionType,
    string? Comment,
    DateTimeOffset DecidedAtUtc);

public interface IApprovalAuditWriter
{
    Task RecordAsync(ApprovalAuditEntry entry, CancellationToken ct);
}
