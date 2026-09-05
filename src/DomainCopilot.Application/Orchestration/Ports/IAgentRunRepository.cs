using DomainCopilot.Domain.Entities;

namespace DomainCopilot.Application.Orchestration.Ports;

public interface IAgentRunRepository
{
    Task AddAsync(AgentRun run, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);

    Task<AgentRun?> GetByIdAsync(Guid runId, CancellationToken ct);

    /// <summary>Prompt 12.5.5: minimal listing for the Approval Queue screen. Returns runs in AwaitingApproval status only, scoped to those the given user initiated.</summary>
    Task<IReadOnlyList<AgentRun>> GetAwaitingApprovalByUserAsync(Guid initiatedByUserId, CancellationToken ct);
}