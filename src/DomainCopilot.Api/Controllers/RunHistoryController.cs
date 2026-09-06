using System.Security.Claims;
using DomainCopilot.Application.Orchestration.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DomainCopilot.Api.Controllers;

/// <summary>
/// History screen: every run the caller has ever initiated, regardless of status,
/// most recent first. Scoped per-Clinician like the Approval Queue (see
/// ApprovalQueueController's docs on that same ownership convention) - an Admin
/// still only sees their own history through this endpoint; there is no separate
/// "all runs" admin view here.
/// </summary>
[ApiController]
[Route("runs")]
[Authorize(Roles = "Clinician")]
public sealed class RunHistoryController : ControllerBase
{
    private readonly IAgentRunRepository _repository;

    public RunHistoryController(IAgentRunRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public sealed record RunHistoryItemDto(
        string RunId,
        Guid ClinicalCaseId,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset? CompletedAt,
        string? TerminationReason);

    [HttpGet("history")]
    public async Task<IActionResult> ListHistory(CancellationToken ct)
    {
        var runs = await _repository.GetHistoryByUserAsync(GetUserId(), ct);

        var dtos = runs.Select(run => new RunHistoryItemDto(
            run.Id.ToString(),
            run.ClinicalCaseId,
            run.Status.ToString(),
            run.CreatedAt,
            run.CompletedAt,
            run.TerminationReason));

        return Ok(dtos);
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated request is missing a NameIdentifier claim.");
        return Guid.Parse(claim.Value);
    }
}