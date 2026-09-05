using System.Security.Claims;
using DomainCopilot.Application.CostGovernor.Ports;
using DomainCopilot.Application.Orchestration.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DomainCopilot.Api.Controllers;

/// <summary>
/// Prompt 12.5.6: full step-by-step trace for a single run (FR-9 requirement -
/// "every run inspectable step-by-step by run id"). Combines AgentRun/AgentStep
/// (via IAgentRunRepository, same pattern as ApprovalQueueController) with
/// UsageRecord data (via ITokenBudgetService.GetUsageByRunAsync, added for this
/// prompt) for token/cost-per-step, and the final ApprovalDecision if one exists.
///
/// Ownership: same BOLA rule as ApprovalWorkflowUseCase (Prompt 12.1) - only the
/// run's own initiator or an Admin may view its trace.
/// </summary>
[ApiController]
[Route("runs")]
[Authorize]
public sealed class TraceController : ControllerBase
{
    private readonly IAgentRunRepository _runRepository;
    private readonly ITokenBudgetService _tokenBudgetService;

    public TraceController(IAgentRunRepository runRepository, ITokenBudgetService tokenBudgetService)
    {
        _runRepository = runRepository ?? throw new ArgumentNullException(nameof(runRepository));
        _tokenBudgetService = tokenBudgetService ?? throw new ArgumentNullException(nameof(tokenBudgetService));
    }

    public sealed record StepTraceDto(
        int StepIndex,
        string AgentRole,
        string? ToolName,
        string Status,
        string Input,
        string? Output,
        string? ErrorMessage,
        string? ModelUsed,
        long TotalTokens,
        decimal EstimatedCostUsd,
        DateTimeOffset? StartedAt,
        DateTimeOffset? CompletedAt);

    public sealed record ApprovalTraceDto(
        string DecisionType, Guid ClinicianUserId, string? EditedContent, string? RejectionReason, DateTimeOffset DecidedAt);

    public sealed record RunTraceDto(
        string RunId,
        Guid ClinicalCaseId,
        string CorrelationId,
        string Status,
        string? TerminationReason,
        DateTimeOffset CreatedAt,
        DateTimeOffset? StartedAt,
        DateTimeOffset? CompletedAt,
        IReadOnlyList<StepTraceDto> Steps,
        long TotalTokens,
        decimal TotalEstimatedCostUsd,
        ApprovalTraceDto? Approval);

    [HttpGet("{runId:guid}/trace")]
    public async Task<IActionResult> GetTrace(Guid runId, CancellationToken ct)
    {
        var run = await _runRepository.GetByIdAsync(runId, ct);
        if (run is null)
            return NotFound();

        if (run.InitiatedByUserId != GetUserId() && !User.IsInRole("Admin"))
            return Forbid();

        var usageRecords = await _tokenBudgetService.GetUsageByRunAsync(runId, ct);
        var usageByStepId = usageRecords
            .Where(u => u.StepId is not null)
            .ToDictionary(u => u.StepId!.Value, u => u);

        var stepDtos = run.Steps
            .OrderBy(s => s.StepIndex)
            .Select(s =>
            {
                usageByStepId.TryGetValue(s.Id, out var usage);
                return new StepTraceDto(
                    s.StepIndex, s.AgentRole.ToString(), s.ToolName, s.Status.ToString(),
                    s.Input, s.Output, s.ErrorMessage, s.ModelUsed,
                    usage?.TotalTokens ?? s.Usage.TotalTokens, usage?.EstimatedCostUsd ?? 0m,
                    s.StartedAt, s.CompletedAt);
            })
            .ToList();

        var approvalDto = run.Approval is null
            ? null
            : new ApprovalTraceDto(
                run.Approval.DecisionType.ToString(), run.Approval.ClinicianUserId,
                run.Approval.EditedContent, run.Approval.RejectionReason, run.Approval.DecidedAt);

        return Ok(new RunTraceDto(
            run.Id.ToString(), run.ClinicalCaseId, run.CorrelationId, run.Status.ToString(), run.TerminationReason,
            run.CreatedAt, run.StartedAt, run.CompletedAt, stepDtos,
            usageRecords.Sum(u => u.TotalTokens), usageRecords.Sum(u => u.EstimatedCostUsd), approvalDto));
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated request is missing a NameIdentifier claim.");
        return Guid.Parse(claim.Value);
    }
}