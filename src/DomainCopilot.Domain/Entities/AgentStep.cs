using DomainCopilot.Domain.Common;
using DomainCopilot.Domain.ValueObjects;

namespace DomainCopilot.Domain.Entities;

/// <summary>
/// A single unit of work performed by one agent (or the orchestrator) within an
/// <see cref="AgentRun"/> — e.g. "Guideline Researcher searches the corpus" or
/// "Safety Checker looks up interactions".
///
/// Invariants:
/// - <see cref="StepIndex"/> is zero-based and reflects execution order within the run
///   (uniqueness within a run is enforced by <see cref="AgentRun.AddStep"/>).
/// - A step cannot be completed, failed, or skipped out of order: it must be
///   <see cref="AgentStepStatus.Running"/> before <see cref="Complete"/> or
///   <see cref="Fail"/>, and must still be <see cref="AgentStepStatus.Pending"/> to be
///   <see cref="Skip"/>ped.
/// - <see cref="Output"/>, <see cref="Usage"/>, and <see cref="ModelUsed"/> are only
///   populated on successful completion; <see cref="ErrorMessage"/> is only populated
///   on failure or skip.
/// - Once a step reaches a terminal status (Succeeded, Failed, Skipped) none of its
///   methods can be called again — it is effectively immutable from that point on.
/// </summary>
public sealed class AgentStep : Entity
{
    public Guid RunId { get; private set; }
    public int StepIndex { get; private set; }
    public AgentRole AgentRole { get; private set; }
    public string? ToolName { get; private set; }
    public string Input { get; private set; } = string.Empty;
    public string? Output { get; private set; }
    public AgentStepStatus Status { get; private set; }
    public string? ModelUsed { get; private set; }
    public TokenUsage Usage { get; private set; } = TokenUsage.Zero;
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private AgentStep()
    {
    }

    private AgentStep(Guid id, Guid runId, int stepIndex, AgentRole agentRole, string input, string? toolName)
        : base(id)
    {
        RunId = runId;
        StepIndex = stepIndex;
        AgentRole = agentRole;
        Input = input;
        ToolName = toolName;
        Status = AgentStepStatus.Pending;
    }

    public static AgentStep Create(Guid runId, int stepIndex, AgentRole agentRole, string input, string? toolName = null)
    {
        if (runId == Guid.Empty)
            throw new ArgumentException("RunId cannot be empty.", nameof(runId));
        if (stepIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(stepIndex), "Step index cannot be negative.");
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Step input cannot be empty.", nameof(input));

        return new AgentStep(Guid.NewGuid(), runId, stepIndex, agentRole, input.Trim(), toolName?.Trim());
    }

    public void Start(DateTimeOffset? startedAt = null)
    {
        if (Status != AgentStepStatus.Pending)
            throw new InvalidOperationException($"Cannot start a step in status {Status}.");

        Status = AgentStepStatus.Running;
        StartedAt = startedAt ?? DateTimeOffset.UtcNow;
    }

    public void Complete(string output, TokenUsage usage, string modelUsed, DateTimeOffset? completedAt = null)
    {
        if (Status != AgentStepStatus.Running)
            throw new InvalidOperationException($"Cannot complete a step in status {Status}; it must be Running.");
        if (string.IsNullOrWhiteSpace(output))
            throw new ArgumentException("Step output cannot be empty.", nameof(output));
        if (string.IsNullOrWhiteSpace(modelUsed))
            throw new ArgumentException("Model used cannot be empty.", nameof(modelUsed));
        ArgumentNullException.ThrowIfNull(usage);

        Output = output;
        Usage = usage;
        ModelUsed = modelUsed.Trim();
        Status = AgentStepStatus.Succeeded;
        CompletedAt = completedAt ?? DateTimeOffset.UtcNow;
    }

    public void Fail(string errorMessage, DateTimeOffset? completedAt = null)
    {
        if (Status != AgentStepStatus.Running)
            throw new InvalidOperationException($"Cannot fail a step in status {Status}; it must be Running.");
        if (string.IsNullOrWhiteSpace(errorMessage))
            throw new ArgumentException("Error message cannot be empty.", nameof(errorMessage));

        ErrorMessage = errorMessage.Trim();
        Status = AgentStepStatus.Failed;
        CompletedAt = completedAt ?? DateTimeOffset.UtcNow;
    }

    public void Skip(string reason, DateTimeOffset? completedAt = null)
    {
        if (Status != AgentStepStatus.Pending)
            throw new InvalidOperationException($"Cannot skip a step in status {Status}; it must be Pending.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Skip reason cannot be empty.", nameof(reason));

        ErrorMessage = reason.Trim();
        Status = AgentStepStatus.Skipped;
        CompletedAt = completedAt ?? DateTimeOffset.UtcNow;
    }
}
