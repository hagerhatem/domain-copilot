using DomainCopilot.Domain.Errors;
using DomainCopilot.Domain.ValueObjects;

namespace DomainCopilot.Application.Agents;

public enum AgentOutcome
{
    Success,
    /// <summary>
    /// Correct, required outcome when evidence is insufficient (Domain Pack D0's
    /// central risk). Carries an <see cref="InsufficientEvidenceError"/> ready to be
    /// passed straight to <see cref="Domain.Entities.AgentRun.Refuse"/> - this is a
    /// value here, not a thrown exception, consistent with how AgentRun.Refuse
    /// already accepts one as a plain parameter.
    /// </summary>
    Refused,
    /// <summary>An unexpected error (infrastructure failure, bug) - distinct from Refused.</summary>
    Failed
}

/// <summary>
/// The outcome of a single agent invocation. Every field needed to later persist an
/// <see cref="Domain.Entities.AgentStep"/> is here (Usage/ModelUsed map directly onto
/// AgentStep.Complete's parameters), but this type does not persist anything itself -
/// the orchestrator is responsible for calling AgentStep.Complete/Fail and, on
/// Refused, AgentRun.Refuse with the carried InsufficientEvidenceError.
///
/// Usage defaults to TokenUsage.Zero and is populated on every outcome (not just
/// Success): a Safety Checker's deterministic lookup with no LLM call at all should
/// report Zero, while an LLM call that itself decided to refuse still consumed real
/// tokens the Cost Governor needs to account for.
/// </summary>
public sealed class AgentResult<TOutput>
{
    public AgentOutcome Outcome { get; }
    public TOutput? Output { get; }
    public InsufficientEvidenceError? RefusalReason { get; }
    public string? FailureMessage { get; }
    public TokenUsage Usage { get; }
    public string? ModelUsed { get; }

    public bool IsSuccess => Outcome == AgentOutcome.Success;

    private AgentResult(
        AgentOutcome outcome,
        TOutput? output,
        InsufficientEvidenceError? refusalReason,
        string? failureMessage,
        TokenUsage usage,
        string? modelUsed)
    {
        Outcome = outcome;
        Output = output;
        RefusalReason = refusalReason;
        FailureMessage = failureMessage;
        Usage = usage;
        ModelUsed = modelUsed;
    }

    public static AgentResult<TOutput> Success(TOutput output, TokenUsage? usage = null, string? modelUsed = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        return new AgentResult<TOutput>(AgentOutcome.Success, output, null, null, usage ?? TokenUsage.Zero, modelUsed);
    }

    public static AgentResult<TOutput> Refused(InsufficientEvidenceError reason, TokenUsage? usage = null, string? modelUsed = null)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return new AgentResult<TOutput>(AgentOutcome.Refused, default, reason, null, usage ?? TokenUsage.Zero, modelUsed);
    }

    public static AgentResult<TOutput> Failed(string failureMessage, TokenUsage? usage = null, string? modelUsed = null)
    {
        if (string.IsNullOrWhiteSpace(failureMessage))
            throw new ArgumentException("Failure message cannot be empty.", nameof(failureMessage));

        return new AgentResult<TOutput>(AgentOutcome.Failed, default, null, failureMessage.Trim(), usage ?? TokenUsage.Zero, modelUsed);
    }
}
