using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainCopilot.Application.CostGovernor.Ports;

public sealed record SpendByUser(Guid UserId, long TotalTokens, decimal TotalEstimatedCostUsd, int RunCount);

public sealed record SpendByAgent(string AgentRole, long TotalTokens, decimal TotalEstimatedCostUsd, int StepCount);

public sealed record AggregatedSpendReport(IReadOnlyList<SpendByUser> ByUser, IReadOnlyList<SpendByAgent> ByAgent);

/// <summary>
/// Prompt 11.2's admin spend view: aggregates UsageRecords two ways. "By agent"
/// requires joining UsageRecords to AgentSteps via StepId (UsageRecords has no
/// AgentRole column of its own) - UsageRecords with a null StepId (the run-level
/// reconciliation row RunClinicalWorkflowUseCase writes, or any future pre-flight-
/// only record) are excluded from the by-agent breakdown, since they aren't
/// attributable to a single agent step. They ARE included in the by-user total, so
/// the two breakdowns' totals will not always match exactly - documented, not a bug.
/// </summary>
public interface ISpendReportService
{
    Task<AggregatedSpendReport> GetAggregatedSpendAsync(CancellationToken ct);
}