using DomainCopilot.Domain.Common;
using DomainCopilot.Domain.Errors;

namespace DomainCopilot.Domain.Entities;

/// <summary>
/// One end-to-end execution of the agentic pipeline (Guideline Researcher → Safety
/// Checker → Documentation Drafter, orchestrated) for a single <see cref="ClinicalCase"/>.
/// This is the aggregate root for a run: <see cref="AgentStep"/> and
/// <see cref="ApprovalDecision"/> are only ever attached to a run through its own
/// methods, never constructed loose and assigned directly.
///
/// Invariants:
/// - <see cref="Steps"/> is append-only and strictly ordered by
///   <see cref="AgentStep.StepIndex"/>; steps can only be added while the run is
///   <see cref="AgentRunStatus.Running"/> (see <see cref="AddStep"/>).
/// - <see cref="Status"/> follows a fixed state machine (see the transition methods
///   below). <see cref="AgentRunStatus.Refused"/> is a distinct, valid terminal state —
///   not a failure — used when the system correctly declines to answer for lack of
///   evidence (Domain Pack D0's central required behavior).
/// - An <see cref="ApprovalDecision"/> can only be applied while the run is
///   <see cref="AgentRunStatus.AwaitingApproval"/>, and only once; any other attempt
///   raises <see cref="InvalidApprovalStateError"/> so the audit trail can never be
///   silently overwritten.
/// - <see cref="CorrelationId"/> is fixed at creation and never changes, so it can be
///   used end-to-end for tracing (request → orchestrator → agent → LLM call).
/// - Once <see cref="Status"/> reaches any terminal value (Completed, Rejected,
///   Refused, Failed, Cancelled) no further state transition is possible.
/// </summary>
public sealed class AgentRun : Entity
{
     private static readonly AgentRunStatus[] TerminalStatuses =
    {
        AgentRunStatus.Completed,
        AgentRunStatus.Rejected,
        AgentRunStatus.Refused,
        AgentRunStatus.Degraded,
        AgentRunStatus.Failed,
        AgentRunStatus.Cancelled
    };

    private readonly List<AgentStep> _steps = new();

    public Guid ClinicalCaseId { get; private set; }
    public Guid InitiatedByUserId { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public AgentRunStatus Status { get; private set; }
    public ApprovalDecision? Approval { get; private set; }
    public string? TerminationReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public IReadOnlyList<AgentStep> Steps => _steps.AsReadOnly();

    private AgentRun()
    {
    }

    private AgentRun(Guid id, Guid clinicalCaseId, Guid initiatedByUserId, string correlationId, DateTimeOffset createdAt)
        : base(id)
    {
        ClinicalCaseId = clinicalCaseId;
        InitiatedByUserId = initiatedByUserId;
        CorrelationId = correlationId;
        Status = AgentRunStatus.Pending;
        CreatedAt = createdAt;
    }

    public static AgentRun Create(Guid clinicalCaseId, Guid initiatedByUserId, string correlationId, DateTimeOffset? createdAt = null)
    {
        if (clinicalCaseId == Guid.Empty)
            throw new ArgumentException("ClinicalCaseId cannot be empty.", nameof(clinicalCaseId));
        if (initiatedByUserId == Guid.Empty)
            throw new ArgumentException("InitiatedByUserId cannot be empty.", nameof(initiatedByUserId));
        if (string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("CorrelationId cannot be empty.", nameof(correlationId));

        return new AgentRun(Guid.NewGuid(), clinicalCaseId, initiatedByUserId, correlationId.Trim(), createdAt ?? DateTimeOffset.UtcNow);
    }

    public void Start(DateTimeOffset? startedAt = null)
    {
        if (Status != AgentRunStatus.Pending)
            throw new InvalidOperationException($"Cannot start a run in status {Status}.");

        Status = AgentRunStatus.Running;
        StartedAt = startedAt ?? DateTimeOffset.UtcNow;
    }

    public void AddStep(AgentStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (Status != AgentRunStatus.Running)
            throw new InvalidOperationException($"Cannot add a step to a run in status {Status}; run must be Running.");
        if (step.RunId != Id)
            throw new InvalidOperationException($"Step {step.Id} belongs to run {step.RunId}, not {Id}.");
        if (_steps.Any(s => s.StepIndex == step.StepIndex))
            throw new InvalidOperationException($"Run {Id} already has a step at index {step.StepIndex}.");

        _steps.Add(step);
    }

    /// <summary>
    /// Moves the run into the human-approval gate once the Documentation Drafter has
    /// produced a draft note. Only a run that is actively Running can request approval.
    /// </summary>
    public void RequestApproval()
    {
        if (Status != AgentRunStatus.Running)
            throw new InvalidOperationException($"Cannot request approval from status {Status}; run must be Running.");

        Status = AgentRunStatus.AwaitingApproval;
    }

    /// <summary>
    /// Applies the Clinician's approve/reject/edit-and-approve decision. This is the
    /// only way an <see cref="ApprovalDecision"/> is ever attached to a run, and it can
    /// only happen once — the previous decision (if any) can never be overwritten.
    /// </summary>
    public void ApplyApprovalDecision(ApprovalDecision decision, DateTimeOffset? completedAt = null)
    {
        ArgumentNullException.ThrowIfNull(decision);

        if (Status != AgentRunStatus.AwaitingApproval)
            throw new InvalidApprovalStateError(Id, "apply an approval decision", Status);
        if (decision.RunId != Id)
            throw new InvalidOperationException($"Decision {decision.Id} belongs to run {decision.RunId}, not {Id}.");
        if (Approval is not null)
            throw new InvalidApprovalStateError(Id, "apply a second approval decision", Status);

        Approval = decision;
        Status = decision.DecisionType == ApprovalDecisionType.Reject
            ? AgentRunStatus.Rejected
            : AgentRunStatus.Completed;
        CompletedAt = completedAt ?? DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Correctly declines to proceed for lack of grounded evidence. This is a distinct,
    /// intentional terminal state — not a failure — and is the mechanism by which
    /// Domain Pack D0's "refuse rather than infer" requirement is enforced end-to-end.
    /// </summary>
    public void Refuse(InsufficientEvidenceError reason, DateTimeOffset? completedAt = null)
    {
        ArgumentNullException.ThrowIfNull(reason);
        if (TerminalStatuses.Contains(Status))
            throw new InvalidOperationException($"Cannot refuse a run already in terminal status {Status}.");

        TerminationReason = reason.Message;
        Status = AgentRunStatus.Refused;
        CompletedAt = completedAt ?? DateTimeOffset.UtcNow;
    }

    public void Fail(string reason, DateTimeOffset? completedAt = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Failure reason cannot be empty.", nameof(reason));
        if (TerminalStatuses.Contains(Status))
            throw new InvalidOperationException($"Cannot fail a run already in terminal status {Status}.");

        TerminationReason = reason.Trim();
        Status = AgentRunStatus.Failed;
        CompletedAt = completedAt ?? DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Correctly completes the run in a reduced-content state after a downstream
    /// agent (Safety Checker or Documentation Drafter) failed even after retries,
    /// while an earlier agent's results are still valid and useful on their own.
    /// See AgentRunStatus.Degraded's XML docs for why this is a distinct outcome
    /// from both Failed and Completed.
    /// </summary>
    public void Degrade(string reason, DateTimeOffset? completedAt = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Degrade reason cannot be empty.", nameof(reason));
        if (TerminalStatuses.Contains(Status))
            throw new InvalidOperationException($"Cannot degrade a run already in terminal status {Status}.");

        TerminationReason = reason.Trim();
        Status = AgentRunStatus.Degraded;
        CompletedAt = completedAt ?? DateTimeOffset.UtcNow;
    }

    public void Cancel(string reason, DateTimeOffset? completedAt = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Cancellation reason cannot be empty.", nameof(reason));
        if (TerminalStatuses.Contains(Status))
            throw new InvalidOperationException($"Cannot cancel a run already in terminal status {Status}.");

        TerminationReason = reason.Trim();
        Status = AgentRunStatus.Cancelled;
        CompletedAt = completedAt ?? DateTimeOffset.UtcNow;
    }
}
