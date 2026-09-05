using DomainCopilot.Application.Ingestion;
using DomainCopilot.Application.Ingestion.Ports;
using DomainCopilot.Domain.Ingestion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DocFormat = DomainCopilot.Domain.Ingestion.DocumentFormat;

namespace DomainCopilot.Api.Controllers;

/// <summary>
/// Prompt 12.5.2: minimal HTTP surface over IngestDocumentUseCase (which already
/// existed in Application but had no controller exposing it). Accepts PDF/DOCX per
/// the mandatory ≥2-input-format requirement. [Authorize] only (no role
/// restriction) - the project brief doesn't scope ingestion to a specific role,
/// unlike ApprovalController's Clinician-only gate.
/// </summary>
[ApiController]
[Route("ingest")]
[Authorize]
public sealed class IngestController : ControllerBase
{
    private readonly IngestDocumentUseCase _useCase;
    private readonly IDocumentRepository _repository;

    public IngestController(IngestDocumentUseCase useCase, IDocumentRepository repository)
    {
        _useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public sealed record DocumentSummaryDto(
        string DocumentId, string FileName, int Version, string Status,
        string? FailureReason, int ChunkCount, DateTimeOffset UpdatedAtUtc);

    [HttpPost]
    [RequestSizeLimit(50_000_000)] // 50MB cap - a guideline PDF/DOCX has no legitimate reason to exceed this
    public async Task<IActionResult> Ingest(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("A non-empty file is required.");

        var format = Path.GetExtension(file.FileName).ToLowerInvariant() switch
        {
            ".pdf" => DocFormat.Pdf,
            ".docx" => DocFormat.Docx,
            _ => (DocFormat?)null
        };

        if (format is null)
            return BadRequest("Only .pdf and .docx files are supported.");

        using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream, ct);

        var source = new DocumentSource(file.FileName, format.Value, memoryStream.ToArray());
        var result = await _useCase.ExecuteAsync(new IngestDocumentCommand(source), ct);

        return Ok(result);
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var documents = await _repository.GetAllAsync(ct);
        var summaries = documents.Select(d => new DocumentSummaryDto(
            d.Id.ToString(), d.FileName, d.Version, d.Status.ToString(),
            d.FailureReason, d.ChunkCount, d.UpdatedAtUtc));

        return Ok(summaries);
    }
}