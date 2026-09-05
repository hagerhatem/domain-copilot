using DomainCopilot.Application.CostGovernor.Ports;
using DomainCopilot.Infrastructure.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DomainCopilot.Infrastructure.CostGovernor;

public sealed class SqlSpendReportService : ISpendReportService
{
    private readonly DomainCopilotDbContext _db;

    public SqlSpendReportService(DomainCopilotDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<AggregatedSpendReport> GetAggregatedSpendAsync(CancellationToken ct)
    {
        var byUser = await _db.UsageRecords
            .GroupBy(u => u.UserId)
            .Select(g => new SpendByUser(
                g.Key,
                g.Sum(u => (long)u.Usage.PromptTokens + u.Usage.CompletionTokens),
                g.Sum(u => u.EstimatedCostUsd),
                g.Select(u => u.RunId).Distinct().Count()))
            .ToListAsync(ct);

        // See ISpendReportService's XML docs: rows with no StepId (run-level
        // reconciliation entries) are excluded here - they can't be attributed to
        // one agent.
        var byAgent = await (
            from u in _db.UsageRecords
            where u.StepId != null
            join s in _db.AgentSteps on u.StepId equals s.Id
            group u by s.AgentRole into g
            select new SpendByAgent(
                g.Key.ToString(),
                g.Sum(u => (long)u.Usage.PromptTokens + u.Usage.CompletionTokens),
                g.Sum(u => u.EstimatedCostUsd),
                g.Count()))
            .ToListAsync(ct);

        return new AggregatedSpendReport(byUser, byAgent);
    }
}