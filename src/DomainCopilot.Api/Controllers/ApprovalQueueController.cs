using System.Security.Claims;
using DomainCopilot.Application.Orchestration.Ports;
using DomainCopilot.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DomainCopilot.Api.Controllers;

/// <summary>
/// Prompt 12.5.5: minimal listing endpoint for the Approval Queue screen (no such
/// GET existed before - IAgentRunRepository previously exposed only
/// AddAsync/GetByIdAsync/SaveChangesAsync). Scoped to runs the caller themselves
/// initiated, consistent with the ownership rule ApprovalWorkflowUseCase enforces
/// on the approve/reject/edit-approve actions themselves (Prompt 12.1 BOLA fix) -
/// an Admin can act on any run via those endpoints, but this LIST view is
/// per-Clinician by design (each Clinician sees their own queue).
///
/// KNOWN GAP, stated honestly: AgentStep.Output for GuidelineResearcher/SafetyChecker
/// steps stores only a numeric summary ("3 excerpt(s) retrieved.", "2 warning(s)
/// found.") - see PipelineOrchestrator.SummarizeOutput - not the actual excerpt
/// text/citations or structured warning data. Those were never persisted anywhere
/// queryable after the run completes. This endpoint surfaces exactly what's
/// available (the summary strings) rather than inventing structured data that
/// doesn't exist. Only the DocumentationDrafter step's Output (the full
/// SUBJECTIVE/ASSESSMENT AND PLAN text) is parsed back into structured fields,
/// since that raw text format is well-defined (see TryParseDraftOutput below,
/// mirroring DocumentationDrafterAgent's own TryParseSections).
/// </summary>
[ApiController]
[Route("runs")]
[Authorize(Roles = "Clinician")]
public sealed class ApprovalQueueController : ControllerBase
{
    private const string SubjectiveMarker = "SUBJECTIVE:";
    private const string PlanMarker = "ASSESSMENT AND PLAN:";

    private readonly IAgentRunRepository _repository;

    public ApprovalQueueController(IAgentRunRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public sealed record StepSummaryDto(int StepIndex, string AgentRole, string Status, string? Output);

    public sealed record PendingApprovalRunDto(
        string RunId,
        Guid ClinicalCaseId,
        string? DraftSubjective,
        string? DraftAssessmentAndPlan,
        IReadOnlyList<StepSummaryDto> Steps);

    [HttpGet("pending-approval")]
    public async Task<IActionResult> ListPendingApproval(CancellationToken ct)
    {
        var runs = await _repository.GetAwaitingApprovalByUserAsync(GetUserId(), ct);

        var dtos = runs.Select(run =>
        {
            var draftStep = run.Steps.SingleOrDefault(s => s.AgentRole == AgentRole.DocumentationDrafter);
            TryParseDraftOutput(draftStep?.Output, out var subjective, out var assessmentAndPlan);

            var stepSummaries = run.Steps
                .OrderBy(s => s.StepIndex)
                .Select(s => new StepSummaryDto(s.StepIndex, s.AgentRole.ToString(), s.Status.ToString(), s.Output))
                .ToList();

            return new PendingApprovalRunDto(run.Id.ToString(), run.ClinicalCaseId, subjective, assessmentAndPlan, stepSummaries);
        });

        return Ok(dtos);
    }

    private static void TryParseDraftOutput(string? text, out string? subjective, out string? assessmentAndPlan)
    {
        subjective = null;
        assessmentAndPlan = null;
        if (string.IsNullOrEmpty(text)) return;

        var subjectiveIndex = text.IndexOf(SubjectiveMarker, StringComparison.OrdinalIgnoreCase);
        var planIndex = text.IndexOf(PlanMarker, StringComparison.OrdinalIgnoreCase);
        if (subjectiveIndex < 0 || planIndex < 0 || planIndex <= subjectiveIndex) return;

        subjective = text[(subjectiveIndex + SubjectiveMarker.Length)..planIndex].Trim();
        assessmentAndPlan = text[(planIndex + PlanMarker.Length)..].Trim();
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated request is missing a NameIdentifier claim.");
        return Guid.Parse(claim.Value);
    }
}