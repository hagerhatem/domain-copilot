using DomainCopilot.Application.Orchestration.Ports;
using DomainCopilot.Domain.Entities;
using DomainCopilot.Infrastructure.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DomainCopilot.Infrastructure.Orchestration;

public sealed class EfAgentRunRepository : IAgentRunRepository
{
    private readonly DomainCopilotDbContext _db;

    public EfAgentRunRepository(DomainCopilotDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task AddAsync(AgentRun run, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(run);
        await _db.AgentRuns.AddAsync(run, ct);
        await _db.SaveChangesAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

    public async Task<AgentRun?> GetByIdAsync(Guid runId, CancellationToken ct) =>
        await _db.AgentRuns
            .Include(r => r.Steps)
            .Include(r => r.Approval)
            .SingleOrDefaultAsync(r => r.Id == runId, ct);

    public async Task<IReadOnlyList<AgentRun>> GetAwaitingApprovalByUserAsync(Guid initiatedByUserId, CancellationToken ct) =>
        await _db.AgentRuns
            .Include(r => r.Steps)
            .Where(r => r.InitiatedByUserId == initiatedByUserId && r.Status == AgentRunStatus.AwaitingApproval)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);
}