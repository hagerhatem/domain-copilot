using DomainCopilot.Application.Agents;
using DomainCopilot.Application.Agents.DocumentationDrafter;
using DomainCopilot.Application.Agents.GuidelineResearcher;
using DomainCopilot.Application.Agents.SafetyChecker;
using DomainCopilot.Application.Common;
using DomainCopilot.Application.CostGovernor.Ports;
using DomainCopilot.Application.Orchestration.Ports;
using DomainCopilot.Domain.Entities;
using DomainCopilot.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace DomainCopilot.Application.Orchestration;

/// <summary>
/// Runs GuidelineResearcherAgent -> SafetyCheckerAgent -> DocumentationDrafterAgent
/// in sequence (FR-5's named Pipeline pattern), persisting an AgentRun with ordered
/// AgentSteps after every stage transition, so a run is inspectable step-by-step by
/// run id (FR-9) even if the process crashes mid-run.
///
/// Prompt 11.2 additions: ILogger calls at every stage transition (correlation id is
/// NOT passed explicitly - it rides Serilog's ambient LogContext, pushed once by
/// CorrelationIdMiddleware for the whole HTTP request; every ILogger call anywhere
/// in this same async flow inherits it automatically). Also calls
/// ITokenBudgetService.RecordStepUsageAsync after every step (for by-agent spend
/// aggregation) and accumulates TotalActualUsage into PipelineRunResult (for
/// RunClinicalWorkflowUseCase's single end-of-run budget reconciliation - see that
/// class's docs for why the two are split).
/// </summary>
public sealed class PipelineOrchestrator
{
    private readonly IGuidelineResearcherAgent _guidelineResearcher;
    private readonly ISafetyCheckerAgent _safetyChecker;
    private readonly IDocumentationDrafterAgent _documentationDrafter;
    private readonly IAgentRunRepository _repository;
    private readonly IClock _clock;
    private readonly OrchestratorOptions _options;
    private readonly IAgentProgressReporter _progressReporter;
    private readonly ITokenBudgetService _tokenBudgetService;
    private readonly ILogger<PipelineOrchestrator> _logger;

    public PipelineOrchestrator(
        IGuidelineResearcherAgent guidelineResearcher,
        ISafetyCheckerAgent safetyChecker,
        IDocumentationDrafterAgent documentationDrafter,
        IAgentRunRepository repository,
        IClock clock,
        OrchestratorOptions options,
        ITokenBudgetService tokenBudgetService,
        ILogger<PipelineOrchestrator> logger,
        IAgentProgressReporter? progressReporter = null)
    {
        _guidelineResearcher = guidelineResearcher ?? throw new ArgumentNullException(nameof(guidelineResearcher));
        _safetyChecker = safetyChecker ?? throw new ArgumentNullException(nameof(safetyChecker));
        _documentationDrafter = documentationDrafter ?? throw new ArgumentNullException(nameof(documentationDrafter));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tokenBudgetService = tokenBudgetService ?? throw new ArgumentNullException(nameof(tokenBudgetService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _progressReporter = progressReporter ?? NullAgentProgressReporter.Instance;
    }

    public async Task<PipelineRunResult> RunAsync(RunClinicalWorkflowCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var run = AgentRun.Create(command.ClinicalCaseId, command.InitiatedByUserId, command.CorrelationId, _clock.UtcNow);
        await _repository.AddAsync(run, ct);
        run.Start(_clock.UtcNow);
        await _repository.SaveChangesAsync(ct);

        _logger.LogInformation("Run {RunId} started for ClinicalCase {ClinicalCaseId}, user {UserId}.",
            run.Id, command.ClinicalCaseId, command.InitiatedByUserId);
        Report(run.Id, AgentProgressEventType.RunStarted, null, "Run started.");

        var totalUsage = TokenUsage.Zero;

        try
        {
            // ---- Step 0: Guideline Researcher --------------------------------------
            var researcherContext = new AgentContext(run.Id, command.ClinicalCaseId, command.CorrelationId);
            var researcherInput = new GuidelineResearcherInput(command.CaseSummary);

            var researcherResult = await RunStepAsync(
                run, 0, AgentRole.GuidelineResearcher, command.CaseSummary, command.InitiatedByUserId,
                ct2 => _guidelineResearcher.ExecuteAsync(researcherContext, researcherInput, ct2), ct);
            totalUsage = Add(totalUsage, researcherResult.Usage);

            if (researcherResult.Outcome == AgentOutcome.Refused)
            {
                run.Refuse(researcherResult.RefusalReason!, _clock.UtcNow);
                await _repository.SaveChangesAsync(ct);
                _logger.LogInformation("Run {RunId} refused at GuidelineResearcher: {Reason}", run.Id, run.TerminationReason);
                Report(run.Id, AgentProgressEventType.RunRefused, AgentRole.GuidelineResearcher, run.TerminationReason!);
                return BuildResult(run, null, null, null, degraded: false, totalUsage);
            }
            if (researcherResult.Outcome == AgentOutcome.Failed)
            {
                run.Fail(researcherResult.FailureMessage ?? "Guideline research failed.", _clock.UtcNow);
                await _repository.SaveChangesAsync(ct);
                _logger.LogWarning("Run {RunId} failed at GuidelineResearcher: {Reason}", run.Id, run.TerminationReason);
                Report(run.Id, AgentProgressEventType.RunFailed, AgentRole.GuidelineResearcher, run.TerminationReason!);
                return BuildResult(run, null, null, null, degraded: false, totalUsage);
            }

            var excerpts = researcherResult.Output!.Excerpts;

            // ---- Step 1: Safety Checker --------------------------------------------
            var checkerContext = new AgentContext(run.Id, command.ClinicalCaseId, command.CorrelationId);
            var checkerInput = new SafetyCheckerInput(command.ProposedMedication, command.CurrentMedications, command.PatientContext);

            var checkerResult = await RunStepAsync(
                run, 1, AgentRole.SafetyChecker, command.ProposedMedication, command.InitiatedByUserId,
                ct2 => _safetyChecker.ExecuteAsync(checkerContext, checkerInput, ct2), ct);
            totalUsage = Add(totalUsage, checkerResult.Usage);

            if (checkerResult.Outcome == AgentOutcome.Refused)
            {
                run.Refuse(checkerResult.RefusalReason!, _clock.UtcNow);
                await _repository.SaveChangesAsync(ct);
                _logger.LogInformation("Run {RunId} refused at SafetyChecker: {Reason}", run.Id, run.TerminationReason);
                Report(run.Id, AgentProgressEventType.RunRefused, AgentRole.SafetyChecker, run.TerminationReason!);
                return BuildResult(run, excerpts, null, null, degraded: false, totalUsage);
            }
            if (checkerResult.Outcome == AgentOutcome.Failed)
            {
                run.Degrade(
                    $"Safety check unavailable after {_options.MaxAttemptsPerAgent} attempts: {checkerResult.FailureMessage}",
                    _clock.UtcNow);
                await _repository.SaveChangesAsync(ct);
                _logger.LogWarning("Run {RunId} degraded at SafetyChecker: {Reason}", run.Id, run.TerminationReason);
                Report(run.Id, AgentProgressEventType.RunDegraded, AgentRole.SafetyChecker, run.TerminationReason!);
                return BuildResult(run, excerpts, null, null, degraded: true, totalUsage);
            }

            var warnings = checkerResult.Output!.Warnings;

            // ---- Step 2: Documentation Drafter -------------------------------------
            var drafterContext = new AgentContext(run.Id, command.ClinicalCaseId, command.CorrelationId);
            var drafterInput = new DocumentationDrafterInput(command.CaseSummary, excerpts, warnings);

            var drafterResult = await RunStepAsync(
                run, 2, AgentRole.DocumentationDrafter, command.CaseSummary, command.InitiatedByUserId,
                ct2 => _documentationDrafter.ExecuteAsync(drafterContext, drafterInput, ct2), ct);
            totalUsage = Add(totalUsage, drafterResult.Usage);

            if (drafterResult.Outcome == AgentOutcome.Refused)
            {
                run.Refuse(drafterResult.RefusalReason!, _clock.UtcNow);
                await _repository.SaveChangesAsync(ct);
                _logger.LogInformation("Run {RunId} refused at DocumentationDrafter: {Reason}", run.Id, run.TerminationReason);
                Report(run.Id, AgentProgressEventType.RunRefused, AgentRole.DocumentationDrafter, run.TerminationReason!);
                return BuildResult(run, excerpts, warnings, null, degraded: false, totalUsage);
            }
            if (drafterResult.Outcome == AgentOutcome.Failed)
            {
                run.Degrade(
                    $"Documentation drafting unavailable after {_options.MaxAttemptsPerAgent} attempts: {drafterResult.FailureMessage}",
                    _clock.UtcNow);
                await _repository.SaveChangesAsync(ct);
                _logger.LogWarning("Run {RunId} degraded at DocumentationDrafter: {Reason}", run.Id, run.TerminationReason);
                Report(run.Id, AgentProgressEventType.RunDegraded, AgentRole.DocumentationDrafter, run.TerminationReason!);
                return BuildResult(run, excerpts, warnings, null, degraded: true, totalUsage);
            }

            // ---- Success: hand off to the human approval gate ----------------------
            run.RequestApproval();
            await _repository.SaveChangesAsync(ct);
            _logger.LogInformation("Run {RunId} completed; awaiting clinician approval. Total usage: {TotalTokens} tokens.",
                run.Id, totalUsage.TotalTokens);
            Report(run.Id, AgentProgressEventType.RunCompleted, AgentRole.DocumentationDrafter, "Run complete; awaiting clinician approval.");

            return BuildResult(run, excerpts, warnings, drafterResult.Output!.Draft, degraded: false, totalUsage);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.Fail(ex.Message, _clock.UtcNow);
            await _repository.SaveChangesAsync(ct);
            _logger.LogError(ex, "Run {RunId} failed with an unhandled exception.", run.Id);
            Report(run.Id, AgentProgressEventType.RunFailed, null, ex.Message);
            return BuildResult(run, null, null, null, degraded: false, totalUsage);
        }
    }

    private async Task<AgentResult<TOutput>> RunStepAsync<TOutput>(
        AgentRun run, int stepIndex, AgentRole role, string input, Guid userId,
        Func<CancellationToken, Task<AgentResult<TOutput>>> stepAction, CancellationToken ct)
    {
        var step = AgentStep.Create(run.Id, stepIndex, role, input);
        run.AddStep(step);
        await _repository.SaveChangesAsync(ct);

        step.Start(_clock.UtcNow);
        await _repository.SaveChangesAsync(ct);
        _logger.LogInformation("Run {RunId} step {StepIndex} ({Role}) started.", run.Id, stepIndex, role);
        Report(run.Id, AgentProgressEventType.AgentStarted, role, $"{role} started.");

        var result = await StepResilienceRunner.RunAsync(stepAction, _options, ct);

        switch (result.Outcome)
        {
            case AgentOutcome.Success:
                step.Complete(SummarizeOutput(result), result.Usage, result.ModelUsed ?? "none", _clock.UtcNow);
                break;
            case AgentOutcome.Refused:
                step.Complete($"REFUSED: {result.RefusalReason!.Message}", result.Usage, result.ModelUsed ?? "none", _clock.UtcNow);
                break;
            case AgentOutcome.Failed:
                step.Fail(result.FailureMessage ?? "Unknown failure.", _clock.UtcNow);
                break;
        }

        await _repository.SaveChangesAsync(ct);

        // Prompt 11.2: per-step usage record for by-agent spend aggregation. Does
        // NOT touch TokenBudget.ConsumedTokens - see ITokenBudgetService's docs on
        // RecordStepUsageAsync for why that stays separate from RecordUsageAsync.
        // EstimatedCostUsd is 0 here deliberately: no per-model $/token pricing
        // table exists anywhere in this codebase yet - tracked as follow-up work,
        // not invented here.
        if (result.Usage.TotalTokens > 0)
        {
            await _tokenBudgetService.RecordStepUsageAsync(
                userId, run.Id, step.Id, result.ModelUsed ?? "none", result.Usage, estimatedCostUsd: 0m, ct);
        }

        _logger.LogInformation("Run {RunId} step {StepIndex} ({Role}) finished: {Outcome}. Tokens: {Tokens}.",
            run.Id, stepIndex, role, result.Outcome, result.Usage.TotalTokens);
        Report(run.Id, AgentProgressEventType.AgentFinished, role, $"{role} finished: {result.Outcome}.");
        return result;
    }

    private void Report(Guid runId, AgentProgressEventType eventType, AgentRole? role, string message) =>
        _progressReporter.Report(new AgentProgressEvent(runId, eventType, role, message, _clock.UtcNow));

    private static TokenUsage Add(TokenUsage a, TokenUsage b) =>
        TokenUsage.Create(a.PromptTokens + b.PromptTokens, a.CompletionTokens + b.CompletionTokens);

    private static string SummarizeOutput<TOutput>(AgentResult<TOutput> result) => result.Output switch
    {
        GuidelineResearcherOutput g => $"{g.Excerpts.Count} excerpt(s) retrieved.",
        SafetyCheckerOutput s => $"{s.Warnings.Count} warning(s) found.",
        DocumentationDrafterOutput d => RenderDraftText(d.Draft),
        _ => "Step completed."
    };

    private static string RenderDraftText(DomainCopilot.Application.Agents.DocumentationDrafter.ClinicalNoteDraft draft) =>
        $"SUBJECTIVE:\n{draft.Subjective}\n\nASSESSMENT AND PLAN:\n{draft.AssessmentAndPlan}";

    private static PipelineRunResult BuildResult(
        AgentRun run,
        IReadOnlyList<GuidelineExcerpt>? excerpts,
        IReadOnlyList<SafetyWarning>? warnings,
        ClinicalNoteDraft? draft,
        bool degraded,
        TokenUsage totalActualUsage) =>
        new(run.Id, run.Status, excerpts, warnings, draft, degraded, degraded ? run.TerminationReason : null, run.TerminationReason, totalActualUsage);
}