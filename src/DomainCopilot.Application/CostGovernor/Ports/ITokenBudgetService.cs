using DomainCopilot.Domain.ValueObjects;

namespace DomainCopilot.Application.CostGovernor.Ports;

public sealed record TokenUsageReconciliation(
    Guid UserId,
    Guid RunId,
    Guid? StepId,
    string ModelName,
    long ReservedEstimateTokens,
    TokenUsage ActualUsage,
    decimal EstimatedCostUsd);

public sealed record UsageRecordSummary(
    Guid RunId,
    Guid? StepId,
    string ModelName,
    long TotalTokens,
    decimal EstimatedCostUsd,
    DateTimeOffset RecordedAt);

public sealed record TokenBudgetStatus(
    Guid UserId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    long AllocatedTokens,
    long ConsumedTokens,
    long RemainingTokens,
    IReadOnlyList<UsageRecordSummary> RecentUsage);

/// <summary>
/// ADR-004's Cost Governor budget service. HasSufficientBudgetAsync is a combined
/// check-AND-reserve operation - not a plain read - executed as ADR-004(d)'s single
/// atomic transaction (SELECT ... WITH (UPDLOCK, ROWLOCK), check, UPDATE, COMMIT).
/// This is what makes it "atomic to prevent race conditions" as a single method call:
/// a concurrent second call for the same user blocks on the row lock until the first
/// call's transaction resolves, so it always observes the already-reserved amount.
///
/// RecordUsageAsync is ADR-004(d) step 5's reconciliation, called after a run
/// completes/fails/degrades: it adjusts the reserved estimate to the actual measured
/// usage (which may be less or more) and writes the immutable UsageRecord audit row -
/// both in one transaction, so the budget number and its audit trail can never
/// diverge.
/// </summary>
public interface ITokenBudgetService
{
    /// <summary>
    /// Atomically checks and, if sufficient, reserves estimatedTokens against the
    /// user's current-period budget. Returns true with the reservation committed, or
    /// false with nothing written (fully rolled back). This IS the hard cut-off -
    /// callers must call this before invoking any agent/LLM work, never after.
    /// </summary>
    Task<bool> HasSufficientBudgetAsync(Guid userId, long estimatedTokens, CancellationToken ct);

    /// <summary>Reconciles a prior reservation to actual usage and records an audit row. See class docs.</summary>
    Task RecordUsageAsync(TokenUsageReconciliation reconciliation, CancellationToken ct);

    /// <summary>Read-only current budget status plus recent usage, for the per-user/per-run/per-agent-step spend view.</summary>
    Task<TokenBudgetStatus> GetStatusAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Prompt 11.2: writes a per-step UsageRecord audit row WITHOUT touching
    /// TokenBudget.ConsumedTokens. Distinct from RecordUsageAsync (which performs
    /// the once-per-run budget reconciliation) - calling RecordUsageAsync once per
    /// step would incorrectly subtract the run's single reserved-estimate multiple
    /// times. Call this after every agent step completes (for by-agent spend
    /// granularity); call RecordUsageAsync exactly once, after the whole run ends,
    /// with the summed actual usage (for correct budget bookkeeping).
    /// </summary>
    Task RecordStepUsageAsync(
        Guid userId, Guid runId, Guid stepId, string modelName, TokenUsage usage, decimal estimatedCostUsd, CancellationToken ct);

    /// <summary>Prompt 12.5.6: all UsageRecord rows for a single run (both per-step rows and the run-level reconciliation row with StepId=null), for the Trace Viewer screen. Ordered by RecordedAt.</summary>
    Task<IReadOnlyList<UsageRecordSummary>> GetUsageByRunAsync(Guid runId, CancellationToken ct);
}
