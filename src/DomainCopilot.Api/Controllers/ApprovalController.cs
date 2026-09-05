using System.Security.Claims;
using DomainCopilot.Application.Orchestration;
using DomainCopilot.Domain.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DomainCopilot.Api.Controllers;

[ApiController]
[Route("runs")]
[Authorize(Roles = "Clinician")]
public sealed class ApprovalController : ControllerBase
{
    private readonly ApprovalWorkflowUseCase _useCase;

    public ApprovalController(ApprovalWorkflowUseCase useCase)
    {
        _useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
    }

    public sealed record RejectRequest(string Comment);
    public sealed record EditApproveRequest(string EditedNoteText);

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        try
        {
            var outcome = await _useCase.ApproveAsync(id, GetClinicianUserId(), IsAdmin(), ct);
            return Ok(outcome);
        }
        catch (AgentRunNotFoundException) { return NotFound(); }
        catch (RunAccessForbiddenException) { return Forbid(); }
        catch (InvalidApprovalStateError ex) { return Conflict(new { ex.Code, ex.Message }); }
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.Comment))
            return BadRequest("A rejection comment is required.");

        try
        {
            var outcome = await _useCase.RejectAsync(id, GetClinicianUserId(), IsAdmin(), request.Comment, ct);
            return Ok(outcome);
        }
        catch (AgentRunNotFoundException) { return NotFound(); }
        catch (RunAccessForbiddenException) { return Forbid(); }
        catch (InvalidApprovalStateError ex) { return Conflict(new { ex.Code, ex.Message }); }
    }

    [HttpPost("{id:guid}/edit-approve")]
    public async Task<IActionResult> EditAndApprove(Guid id, [FromBody] EditApproveRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.EditedNoteText))
            return BadRequest("Edited note text is required.");

        try
        {
            var outcome = await _useCase.EditAndApproveAsync(id, GetClinicianUserId(), IsAdmin(), request.EditedNoteText, ct);
            return Ok(outcome);
        }
        catch (AgentRunNotFoundException) { return NotFound(); }
        catch (RunAccessForbiddenException) { return Forbid(); }
        catch (InvalidApprovalStateError ex) { return Conflict(new { ex.Code, ex.Message }); }
    }

    private bool IsAdmin() => User.IsInRole("Admin");

    private Guid GetClinicianUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated request is missing a NameIdentifier claim.");
        return Guid.Parse(claim.Value);
    }
}