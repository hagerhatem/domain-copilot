using DomainCopilot.Domain.Entities;

namespace DomainCopilot.Domain.Errors;

/// <summary>
/// Raised when an <see cref="Entities.ApprovalDecision"/> is applied to an
/// <see cref="Entities.AgentRun"/> that is not in a state that can legally accept it —
/// for example, approving a run that never reached
/// <see cref="AgentRunStatus.AwaitingApproval"/>, or recording a second decision
/// against a run that already has one.
///
/// Enforcing this as a domain error (rather than silently overwriting or ignoring the
/// attempt) keeps the human-approval audit trail trustworthy: every clinical note that
/// was ever finalized must be traceable to exactly one valid approval decision, made
/// while the run was genuinely waiting for it.
/// </summary>
public sealed class InvalidApprovalStateError : DomainError
{
    public Guid RunId { get; }
    public string AttemptedTransition { get; }
    public AgentRunStatus CurrentStatus { get; }

    public InvalidApprovalStateError(Guid runId, string attemptedTransition, AgentRunStatus currentStatus)
        : base("INVALID_APPROVAL_STATE", BuildMessage(runId, attemptedTransition, currentStatus))
    {
        if (string.IsNullOrWhiteSpace(attemptedTransition))
            throw new ArgumentException("Attempted transition description cannot be empty.", nameof(attemptedTransition));

        RunId = runId;
        AttemptedTransition = attemptedTransition;
        CurrentStatus = currentStatus;
    }

    private static string BuildMessage(Guid runId, string attemptedTransition, AgentRunStatus currentStatus) =>
        $"Cannot {attemptedTransition} for run {runId}: run is currently {currentStatus}.";
}
