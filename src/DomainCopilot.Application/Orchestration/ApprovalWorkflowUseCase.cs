using DomainCopilot.Application.Common;
using DomainCopilot.Application.Orchestration.Ports;
using DomainCopilot.Domain.Entities;

namespace DomainCopilot.Application.Orchestration;

public sealed record ApprovalOutcome(Guid RunId, AgentRunStatus Status);

/// <summary>
/// Implements Prompt 8.2's three approval actions. State-transition legality
/// (must be AwaitingApproval, at most one decision ever) is NOT re-checked here -
/// AgentRun.ApplyApprovalDecision already throws
/// DomainCopilot.Domain.Errors.InvalidApprovalStateError itself if the run isn't in
/// a state that can legally accept a decision.
///
/// SECURITY (Prompt 12.1 / OWASP A01 BOLA): callerIsAdmin aside, only the Clinician
/// who initiated a run (AgentRun.InitiatedByUserId) may approve/reject/edit-approve
/// it. Enforced here, BEFORE any state is read for decision-building or mutated -
/// this is the single authoritative ownership check; the controller must not
/// attempt its own parallel check that could drift out of sync with this one.
/// </summary>
public sealed class ApprovalWorkflowUseCase
{
    private readonly IAgentRunRepository _repository;
    private readonly IApprovalAuditWriter _auditWriter;
    private readonly IClock _clock;

    public ApprovalWorkflowUseCase(IAgentRunRepository repository, IApprovalAuditWriter auditWriter, IClock clock)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public Task<ApprovalOutcome> ApproveAsync(Guid runId, Guid clinicianUserId, bool callerIsAdmin, CancellationToken ct) =>
        ApplyAsync(runId, clinicianUserId, callerIsAdmin, "Approve", comment: null, ct,
            (run, draftText, decidedAt) => ApprovalDecision.Approve(run.Id, clinicianUserId, draftText, decidedAt));

    public Task<ApprovalOutcome> RejectAsync(Guid runId, Guid clinicianUserId, bool callerIsAdmin, string comment, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(comment))
            throw new ArgumentException("A rejection comment is required.", nameof(comment));

        return ApplyAsync(runId, clinicianUserId, callerIsAdmin, "Reject", comment, ct,
            (run, draftText, decidedAt) => ApprovalDecision.Reject(run.Id, clinicianUserId, draftText, comment, decidedAt));
    }

    public Task<ApprovalOutcome> EditAndApproveAsync(
        Guid runId, Guid clinicianUserId, bool callerIsAdmin, string editedNoteText, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(editedNoteText))
            throw new ArgumentException("Edited note text is required.", nameof(editedNoteText));

        return ApplyAsync(runId, clinicianUserId, callerIsAdmin, "EditAndApprove", comment: null, ct,
            (run, draftText, decidedAt) => ApprovalDecision.EditAndApprove(run.Id, clinicianUserId, draftText, editedNoteText, decidedAt));
    }

    private async Task<ApprovalOutcome> ApplyAsync(
        Guid runId,
        Guid clinicianUserId,
        bool callerIsAdmin,
        string decisionTypeLabel,
        string? comment,
        CancellationToken ct,
        Func<AgentRun, string, DateTimeOffset, ApprovalDecision> buildDecision)
    {
        var run = await _repository.GetByIdAsync(runId, ct) ?? throw new AgentRunNotFoundException(runId);

        // Ownership check FIRST - before reading draft text, building a decision,
        // or touching AgentRun's state machine at all.
        if (!callerIsAdmin && run.InitiatedByUserId != clinicianUserId)
            throw new RunAccessForbiddenException(runId, clinicianUserId);

        var previousStatus = run.Status.ToString();
        var draftText = ExtractDraftText(run);
        var decidedAt = _clock.UtcNow;

        var decision = buildDecision(run, draftText, decidedAt);

        run.ApplyApprovalDecision(decision, decidedAt);

        await _auditWriter.RecordAsync(
            new ApprovalAuditEntry(runId, clinicianUserId, previousStatus, decisionTypeLabel, comment, decidedAt), ct);

        await _repository.SaveChangesAsync(ct);

        return new ApprovalOutcome(run.Id, run.Status);
    }

    private static string ExtractDraftText(AgentRun run)
    {
        var draftStep = run.Steps.SingleOrDefault(
            s => s.AgentRole == AgentRole.DocumentationDrafter && s.Status == AgentStepStatus.Succeeded);

        return draftStep?.Output
            ?? throw new InvalidOperationException(
                $"AgentRun '{run.Id}' has no completed DocumentationDrafter step to approve - " +
                "this indicates a bug (RequestApproval should never be called without one).");
    }
}