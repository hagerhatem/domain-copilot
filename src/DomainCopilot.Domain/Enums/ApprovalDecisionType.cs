namespace DomainCopilot.Domain.Entities;

/// <summary>The three outcomes a Clinician can record against a drafted clinical note.</summary>
public enum ApprovalDecisionType
{
    Approve,
    Reject,
    EditAndApprove
}
