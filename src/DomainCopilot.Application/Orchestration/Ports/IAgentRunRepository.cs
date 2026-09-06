using DomainCopilot.Domain.Entities;

namespace DomainCopilot.Application.Orchestration.Ports;

public interface IAgentRunRepository
{
    Task AddAsync(AgentRun run, CancellationToken ct);

    /// <summary>
    /// Explicitly registers a new AgentStep with the change tracker as Added.
    /// Required because AgentStep.Id is a client-generated Guid (see
    /// AgentStep.Create) and the step only ever reaches the DbContext through
    /// AgentRun.AddStep's private _steps field (navigation fixup), never through
    /// a direct DbSet.Add call - so without this, EF Core's default Guid key
    /// convention treats it as an existing (Unchanged/Modified) row instead of
    /// a new one, producing a silent DbUpdateConcurrencyException on SaveChanges.
    /// </summary>
    Task AddStepAsync(AgentStep step, CancellationToken ct);

    /// <summary>
    /// Explicitly registers a new ApprovalDecision with the change tracker as
    /// Added. Same reason as AddStepAsync: ApprovalDecision.Id is a
    /// client-generated Guid (see ApprovalDecision.CreateInternal) and it only
    /// ever reaches the DbContext through AgentRun.ApplyApprovalDecision's
    /// Approval property (navigation fixup), never through a direct DbSet.Add
    /// call - without this, EF Core treats it as an existing row instead of a
    /// new one, producing a silent DbUpdateConcurrencyException on SaveChanges.
    /// </summary>
    Task AddApprovalDecisionAsync(ApprovalDecision decision, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);

    Task<AgentRun?> GetByIdAsync(Guid runId, CancellationToken ct);

    /// <summary>Prompt 12.5.5: minimal listing for the Approval Queue screen. Returns runs in AwaitingApproval status only, scoped to those the given user initiated.</summary>
    Task<IReadOnlyList<AgentRun>> GetAwaitingApprovalByUserAsync(Guid initiatedByUserId, CancellationToken ct);

    /// <summary>
    /// Full run history for the History screen: every run the given user initiated,
    /// regardless of status, most recent first. Unlike GetAwaitingApprovalByUserAsync
    /// this is not filtered to AwaitingApproval - it includes runs still Running as
    /// well as every terminal status (Completed, Rejected, Refused, Degraded, Failed,
    /// Cancelled).
    /// </summary>
    Task<IReadOnlyList<AgentRun>> GetHistoryByUserAsync(Guid initiatedByUserId, CancellationToken ct);
}