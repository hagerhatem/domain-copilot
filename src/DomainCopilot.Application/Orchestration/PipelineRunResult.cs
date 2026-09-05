using DomainCopilot.Application.Agents.DocumentationDrafter;
using DomainCopilot.Application.Agents.GuidelineResearcher;
using DomainCopilot.Application.Agents.SafetyChecker;
using DomainCopilot.Domain.Entities;

namespace DomainCopilot.Application.Orchestration;

public sealed record RunClinicalWorkflowCommand(
    Guid ClinicalCaseId,
    Guid InitiatedByUserId,
    string CorrelationId,
    string CaseSummary,
    string ProposedMedication,
    IReadOnlyList<string> CurrentMedications,
    string? PatientContext);

/// <summary>
/// What the orchestrator hands back once the run reaches a terminal or
/// approval-pending state. Fields are populated incrementally as far as the pipeline
/// got before stopping - e.g. a run that Refused at the Safety Checker step still
/// carries GuidelineExcerpts (Step 0 succeeded) but null SafetyWarnings/Draft.
///
/// DegradedMode is the FR-5 "graceful degradation" signal: true means a downstream
/// agent failed after exhausting retries, but earlier results were still valid and
/// are being returned anyway rather than discarding a partial answer or crashing the
/// run. Callers (Api controllers, UI) MUST surface DegradedMode prominently - never
/// present a degraded result as if it were a complete, fully-checked answer.
/// </summary>
public sealed record PipelineRunResult(
    Guid RunId,
    AgentRunStatus Status,
    IReadOnlyList<GuidelineExcerpt>? GuidelineExcerpts,
    IReadOnlyList<SafetyWarning>? SafetyWarnings,
    ClinicalNoteDraft? Draft,
    bool DegradedMode,
    string? DegradedModeReason,
    string? TerminationReason,
    DomainCopilot.Domain.ValueObjects.TokenUsage TotalActualUsage);
