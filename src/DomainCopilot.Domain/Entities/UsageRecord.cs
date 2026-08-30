using DomainCopilot.Domain.Common;
using DomainCopilot.Domain.ValueObjects;

namespace DomainCopilot.Domain.Entities;

/// <summary>
/// An immutable audit record of a single LLM call's usage and estimated cost, feeding
/// the Cost Governor's spend view (per-user historical spend broken down by run and by
/// agent step).
///
/// Invariants:
/// - Never mutated after creation — usage history is strictly append-only.
/// - <see cref="StepId"/> is nullable to allow recording usage for calls made outside
///   any single agent step (e.g. pre-flight cost estimation), but when present it is
///   expected to belong to the same <see cref="RunId"/> — the Application layer, which
///   has visibility of both aggregates together, is responsible for that check.
/// - <see cref="EstimatedCostUsd"/> is never negative.
/// </summary>
public sealed class UsageRecord : Entity
{
    public Guid RunId { get; private set; }
    public Guid? StepId { get; private set; }
    public Guid UserId { get; private set; }
    public string ModelName { get; private set; } = string.Empty;
    public TokenUsage Usage { get; private set; } = TokenUsage.Zero;
    public decimal EstimatedCostUsd { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }

    private UsageRecord()
    {
    }

    private UsageRecord(
        Guid id,
        Guid runId,
        Guid? stepId,
        Guid userId,
        string modelName,
        TokenUsage usage,
        decimal estimatedCostUsd,
        DateTimeOffset recordedAt) : base(id)
    {
        RunId = runId;
        StepId = stepId;
        UserId = userId;
        ModelName = modelName;
        Usage = usage;
        EstimatedCostUsd = estimatedCostUsd;
        RecordedAt = recordedAt;
    }

    public static UsageRecord Create(
        Guid runId,
        Guid userId,
        string modelName,
        TokenUsage usage,
        decimal estimatedCostUsd,
        Guid? stepId = null,
        DateTimeOffset? recordedAt = null)
    {
        if (runId == Guid.Empty)
            throw new ArgumentException("RunId cannot be empty.", nameof(runId));
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        if (string.IsNullOrWhiteSpace(modelName))
            throw new ArgumentException("Model name cannot be empty.", nameof(modelName));
        ArgumentNullException.ThrowIfNull(usage);
        if (estimatedCostUsd < 0)
            throw new ArgumentOutOfRangeException(nameof(estimatedCostUsd), "Estimated cost cannot be negative.");

        return new UsageRecord(Guid.NewGuid(), runId, stepId, userId, modelName.Trim(), usage, estimatedCostUsd, recordedAt ?? DateTimeOffset.UtcNow);
    }
}
