using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainCopilot.Application.Orchestration;

/// <summary>
/// Configurable pre-flight cost-estimation heuristic for RunClinicalWorkflowUseCase
/// (Twist T3 / Prompt 9.3). EstimatedTokensPerRun is a fixed value, not yet a
/// computed historical average - see RunClinicalWorkflowUseCase's XML docs for why.
/// </summary>
public sealed class WorkflowCostEstimationOptions
{
    public const string SectionName = "WorkflowCostEstimation";

    /// <summary>
    /// Conservative fixed estimate of total tokens (prompt + completion, across all
    /// three agent steps) a single clinical workflow run is expected to consume.
    /// Set high enough to cover a full GuidelineResearcher retry loop (up to 3 query
    /// reformulations) plus a DocumentationDrafter format retry (up to 2 attempts)
    /// at typical prompt sizes for this corpus - tune based on real UsageRecord data
    /// once enough completed runs exist to compute a genuine historical average.
    /// </summary>
    public long EstimatedTokensPerRun { get; set; } = 6000;
}
