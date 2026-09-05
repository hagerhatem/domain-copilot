namespace DomainCopilot.Infrastructure.Orchestration;

/// <summary>
/// The dedicated ApprovalAudit table Prompt 8.2 asks for - a plain, append-only
/// persistence record (no business behavior, so not a Domain entity, matching the
/// same reasoning as DrugInteractionRuleEntity). Never updated or deleted after
/// insert; one row per approve/reject/edit-approve action, always written regardless
/// of which of the three actions occurred.
/// </summary>
public sealed class ApprovalAuditEntity
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public Guid ClinicianUserId { get; set; }
    public string PreviousStatus { get; set; } = string.Empty;
    public string DecisionType { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public DateTimeOffset DecidedAtUtc { get; set; }
}
