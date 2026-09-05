using System.Security.Claims;
using System.Text.Json;
using DomainCopilot.Api.Correlation;
using DomainCopilot.Api.Streaming;
using DomainCopilot.Application.Orchestration;
using DomainCopilot.Application.Orchestration.Ports;
using DomainCopilot.Domain.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DomainCopilot.Api.Controllers;

[ApiController]
[Route("runs")]
[Authorize(Roles = "Clinician")]
public sealed class RunsController : ControllerBase
{
    private readonly IClinicalCaseRepository _clinicalCaseRepository;
    private readonly RunClinicalWorkflowUseCase _useCase;
    private readonly ChannelAgentProgressReporter _progressReporter;
    private readonly IRunCancellationRegistry _cancellationRegistry;

    public RunsController(
        IClinicalCaseRepository clinicalCaseRepository,
        RunClinicalWorkflowUseCase useCase,
        IAgentProgressReporter progressReporter,
        IRunCancellationRegistry cancellationRegistry)
    {
        _clinicalCaseRepository = clinicalCaseRepository ?? throw new ArgumentNullException(nameof(clinicalCaseRepository));
        _useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
        _cancellationRegistry = cancellationRegistry ?? throw new ArgumentNullException(nameof(cancellationRegistry));

        _progressReporter = progressReporter as ChannelAgentProgressReporter
            ?? throw new InvalidOperationException(
                $"{nameof(RunsController)} requires {nameof(IAgentProgressReporter)} to be a {nameof(ChannelAgentProgressReporter)}.");
    }

    [HttpGet("{clinicalCaseId:guid}/stream")]
    public async Task Stream(Guid clinicalCaseId, CancellationToken ct)
    {
        var clinicalCase = await _clinicalCaseRepository.GetByIdAsync(clinicalCaseId, ct);
        if (clinicalCase is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // SECURITY (Prompt 12.1 / OWASP A01 BOLA): a Clinician may only start/stream
        // a run against a ClinicalCase they created. Checked BEFORE any budget
        // reservation or agent work starts.
        var callerUserId = GetUserId();
        if (clinicalCase.CreatedByUserId != callerUserId && !IsAdmin())
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Append("X-Accel-Buffering", "no");

        var command = new RunClinicalWorkflowCommand(
            clinicalCase.Id,
            callerUserId,
            HttpContext.GetCorrelationId(),
            clinicalCase.PresentingComplaint,
            clinicalCase.ProposedMedication,
            clinicalCase.Medications.ToList(),
            clinicalCase.PatientContext);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Guid? registeredRunId = null;

        var runTask = RunAndSignalCompletionAsync(command, linkedCts.Token);

        try
        {
            await foreach (var progressEvent in _progressReporter.Reader.ReadAllAsync(ct))
            {
                if (registeredRunId is null && progressEvent.RunId != Guid.Empty)
                {
                    registeredRunId = progressEvent.RunId;
                    _cancellationRegistry.Register(progressEvent.RunId, callerUserId, linkedCts);
                }

                var json = JsonSerializer.Serialize(progressEvent);
                await Response.WriteAsync($"event: progress\ndata: {json}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        finally
        {
            if (registeredRunId is not null)
                _cancellationRegistry.Unregister(registeredRunId.Value);
        }

        await runTask;
    }

    [HttpDelete("{runId:guid}")]
    public IActionResult Cancel(Guid runId)
    {
        var result = _cancellationRegistry.TryCancel(runId, GetUserId(), IsAdmin());
        return result switch
        {
            RunCancellationResult.Cancelled => Accepted(),
            RunCancellationResult.Forbidden => Forbid(),
            _ => NotFound()
        };
    }

    private async Task RunAndSignalCompletionAsync(RunClinicalWorkflowCommand command, CancellationToken ct)
    {
        try
        {
            await _useCase.ExecuteAsync(command, ct);
            _progressReporter.Complete();
        }
        catch (BudgetExceededError ex)
        {
            _progressReporter.Report(new AgentProgressEvent(
                Guid.Empty, AgentProgressEventType.RunFailed, null, ex.Message, DateTimeOffset.UtcNow));
            _progressReporter.Complete();
        }
        catch (OperationCanceledException)
        {
            _progressReporter.Complete();
            throw;
        }
    }

    private bool IsAdmin() => User.IsInRole("Admin");

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated request is missing a NameIdentifier claim.");
        return Guid.Parse(claim.Value);
    }
}