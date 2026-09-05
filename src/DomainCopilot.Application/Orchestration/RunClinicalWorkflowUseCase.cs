using DomainCopilot.Application.CostGovernor.Ports;
using DomainCopilot.Domain.Errors;
using Microsoft.Extensions.Logging;

namespace DomainCopilot.Application.Orchestration;

/// <summary>
/// Prompt 9.3's pre-flight cut-off wrapper, extended by Prompt 11.2 to perform the
/// SINGLE end-of-run budget reconciliation (RecordUsageAsync) once the orchestrator
/// returns - using the ACTUAL total usage across all three steps
/// (PipelineRunResult.TotalActualUsage) against the ORIGINAL reserved estimate
/// (estimatedTokens, computed below, same value passed to
/// HasSufficientBudgetAsync). Per-step audit rows (for by-agent spend aggregation)
/// are written separately, inside PipelineOrchestrator itself, via
/// RecordStepUsageAsync - see that method's docs for why the two must not be
/// conflated.
/// </summary>
public sealed class RunClinicalWorkflowUseCase
{
    private readonly PipelineOrchestrator _orchestrator;
    private readonly ITokenBudgetService _tokenBudgetService;
    private readonly WorkflowCostEstimationOptions _options;
    private readonly ILogger<RunClinicalWorkflowUseCase> _logger;

    public RunClinicalWorkflowUseCase(
        PipelineOrchestrator orchestrator,
        ITokenBudgetService tokenBudgetService,
        WorkflowCostEstimationOptions options,
        ILogger<RunClinicalWorkflowUseCase> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _tokenBudgetService = tokenBudgetService ?? throw new ArgumentNullException(nameof(tokenBudgetService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PipelineRunResult> ExecuteAsync(RunClinicalWorkflowCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var estimatedTokens = _options.EstimatedTokensPerRun;

        var hasSufficientBudget = await _tokenBudgetService.HasSufficientBudgetAsync(
            command.InitiatedByUserId, estimatedTokens, ct);

        if (!hasSufficientBudget)
        {
            var status = await _tokenBudgetService.GetStatusAsync(command.InitiatedByUserId, ct);
            _logger.LogWarning("Budget exceeded for user {UserId}: requested {Requested}, remaining {Remaining}.",
                command.InitiatedByUserId, estimatedTokens, status.RemainingTokens);
            throw new BudgetExceededError(command.InitiatedByUserId, estimatedTokens, status.RemainingTokens);
        }

        var result = await _orchestrator.RunAsync(command, ct);

        // Single end-of-run reconciliation against the reservation made above -
        // corrects TokenBudget.ConsumedTokens to the real total (may be less or
        // more than estimatedTokens) and writes one run-level audit row (StepId:
        // null - per-step rows already exist from PipelineOrchestrator).
        await _tokenBudgetService.RecordUsageAsync(
     new TokenUsageReconciliation(
         UserId: command.InitiatedByUserId,
         RunId: result.RunId,
         StepId: null,
         ModelName: "run-total",
         ReservedEstimateTokens: estimatedTokens,
         ActualUsage: result.TotalActualUsage,
         EstimatedCostUsd: 0m),
     ct);

        return result;
    }
}