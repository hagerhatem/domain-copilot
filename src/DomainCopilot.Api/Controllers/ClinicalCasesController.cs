using System.Security.Claims;
using DomainCopilot.Application.Orchestration.Ports;
using DomainCopilot.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DomainCopilot.Api.Controllers;

/// <summary>
/// Prompt 12.5.4 follow-up: minimal HTTP surface over ClinicalCase (previously
/// creatable only via direct DB seeding). Lets the frontend create a case and pick
/// from its own list instead of pasting a raw ClinicalCaseId into the Run Workflow
/// screen.
/// </summary>
[ApiController]
[Route("clinical-cases")]
[Authorize(Roles = "Clinician")]
public sealed class ClinicalCasesController : ControllerBase
{
    private readonly IClinicalCaseRepository _repository;

    public ClinicalCasesController(IClinicalCaseRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public sealed record CreateClinicalCaseRequest(
        string CaseReference,
        string PresentingComplaint,
        string ProposedMedication,
        string? PatientContext,
        IReadOnlyList<string>? CurrentMedications);

    public sealed record ClinicalCaseSummaryDto(
        string Id, string CaseReference, string PresentingComplaint,
        string ProposedMedication, DateTimeOffset CreatedAt);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateClinicalCaseRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CaseReference) ||
            string.IsNullOrWhiteSpace(request.PresentingComplaint) ||
            string.IsNullOrWhiteSpace(request.ProposedMedication))
        {
            return BadRequest("CaseReference, PresentingComplaint, and ProposedMedication are required.");
        }

        var clinicalCase = ClinicalCase.Create(
            request.CaseReference,
            request.PresentingComplaint,
            request.ProposedMedication,
            GetUserId(),
            request.PatientContext);

        foreach (var medication in request.CurrentMedications ?? Array.Empty<string>())
            clinicalCase.AddMedication(medication);

        await _repository.AddAsync(clinicalCase, ct);

        return Ok(new ClinicalCaseSummaryDto(
            clinicalCase.Id.ToString(), clinicalCase.CaseReference, clinicalCase.PresentingComplaint,
            clinicalCase.ProposedMedication, clinicalCase.CreatedAt));
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var cases = await _repository.GetAllByUserAsync(GetUserId(), ct);
        var summaries = cases.Select(c => new ClinicalCaseSummaryDto(
            c.Id.ToString(), c.CaseReference, c.PresentingComplaint, c.ProposedMedication, c.CreatedAt));

        return Ok(summaries);
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated request is missing a NameIdentifier claim.");
        return Guid.Parse(claim.Value);
    }
}