using DomainCopilot.Application.Retrieval;
using DomainCopilot.Application.Retrieval.Ports;
using DomainCopilot.Domain.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DomainCopilot.Api.Controllers;

/// <summary>
/// Prompt 12.5.3: minimal HTTP surface over hybrid retrieval + EvidenceSufficiencyChecker
/// (both existed already in Application/Infrastructure with no controller exposing
/// them). Deliberately does NOT call an LLM to synthesize a prose "answer" - no
/// AskQuestionUseCase exists yet (see EvidenceSufficiencyChecker's own XML docs,
/// which name that use case as future work). This endpoint returns the grounded,
/// cited excerpts themselves as the "answer" - each excerpt IS the evidence, not a
/// paraphrase of it, consistent with Domain Pack D0's "refuse rather than
/// hallucinate" principle: nothing here is generated, so nothing here can be
/// ungrounded.
/// </summary>
[ApiController]
[Route("ask")]
[Authorize]
public sealed class AskController : ControllerBase
{
    private readonly IHybridRetrievalService _retrievalService;
    private readonly EvidenceSufficiencyChecker _evidenceSufficiencyChecker;

    public AskController(IHybridRetrievalService retrievalService, EvidenceSufficiencyChecker evidenceSufficiencyChecker)
    {
        _retrievalService = retrievalService ?? throw new ArgumentNullException(nameof(retrievalService));
        _evidenceSufficiencyChecker = evidenceSufficiencyChecker ?? throw new ArgumentNullException(nameof(evidenceSufficiencyChecker));
    }

    public sealed record AskRequest(string Question, int TopK = 10);

    public sealed record CitationDto(string SourceDocumentFileName, string? Section, int? PageNumber, string Text, double FusedScore);

    public sealed record AskResponse(string Question, IReadOnlyList<CitationDto> Citations);

    /// <summary>Mirrors InsufficientEvidenceError's own fields exactly - Code/Message from DomainError, plus Query/Reason - so the frontend can render a refusal state distinct from a normal answer without guessing at field names.</summary>
    public sealed record InsufficientEvidenceDto(string Code, string Message, string Query, string Reason);

    [HttpPost]
    public async Task<IActionResult> Ask([FromBody] AskRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest("Question cannot be empty.");

        var retrievalResult = await _retrievalService.RetrieveAsync(
            new RetrievalQuery(request.Question, request.TopK), ct);

        if (retrievalResult.IsFailure)
            return StatusCode(StatusCodes.Status502BadGateway, new { retrievalResult.Error!.Code, retrievalResult.Error.Message });

        var sufficiency = _evidenceSufficiencyChecker.Check(request.Question, retrievalResult.Value);

        if (!sufficiency.IsSufficient)
        {
            var error = sufficiency.RefusalError!;
            return Conflict(new InsufficientEvidenceDto(error.Code, error.Message, error.Query, error.Reason.ToString()));
        }

        var citations = sufficiency.SupportingChunks
            .Select(c => new CitationDto(c.SourceDocumentFileName, c.Section, c.PageNumber, c.Text, c.FusedScore))
            .ToList();

        return Ok(new AskResponse(request.Question, citations));
    }
}