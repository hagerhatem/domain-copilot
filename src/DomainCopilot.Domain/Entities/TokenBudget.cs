using DomainCopilot.Domain.Common;
using DomainCopilot.Domain.Errors;

namespace DomainCopilot.Domain.Entities;

/// <summary>
/// A user's token allowance for a given period — the Cost Governor's hard-cutoff
/// mechanism. This is the single deterministic source of truth for "can this user run
/// anything else right now"; it must be checked/consumed server-side and must never be
/// inferable or overridable from client input.
///
/// Invariants:
/// - <see cref="ConsumedTokens"/> can only increase, only through <see cref="Consume"/>,
///   and can never exceed <see cref="AllocatedTokens"/> — enforced by raising
///   <see cref="BudgetExceededError"/> rather than silently clamping, so a caller can
///   never accidentally "succeed" at partially consuming an over-budget request.
/// - <see cref="PeriodEnd"/> is always strictly after <see cref="PeriodStart"/>.
/// - <see cref="RemainingTokens"/> is always <c>AllocatedTokens - ConsumedTokens</c> and
///   is never negative.
/// </summary>
public sealed class TokenBudget : Entity
{
    public Guid UserId { get; private set; }
    public DateTimeOffset PeriodStart { get; private set; }
    public DateTimeOffset PeriodEnd { get; private set; }
    public long AllocatedTokens { get; private set; }
    public long ConsumedTokens { get; private set; }

    public long RemainingTokens => AllocatedTokens - ConsumedTokens;

    private TokenBudget()
    {
    }

    private TokenBudget(Guid id, Guid userId, DateTimeOffset periodStart, DateTimeOffset periodEnd, long allocatedTokens)
        : base(id)
    {
        UserId = userId;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        AllocatedTokens = allocatedTokens;
        ConsumedTokens = 0;
    }

    public static TokenBudget Create(Guid userId, DateTimeOffset periodStart, DateTimeOffset periodEnd, long allocatedTokens)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        if (periodEnd <= periodStart)
            throw new ArgumentException("PeriodEnd must be after PeriodStart.", nameof(periodEnd));
        if (allocatedTokens < 0)
            throw new ArgumentOutOfRangeException(nameof(allocatedTokens), "Allocated tokens cannot be negative.");

        return new TokenBudget(Guid.NewGuid(), userId, periodStart, periodEnd, allocatedTokens);
    }

    /// <summary>
    /// Pre-flight check (does NOT mutate state): can this many additional tokens still
    /// be spent in this budget period? Used by the pre-flight cost estimator before a
    /// workflow run is even started.
    /// </summary>
    public bool HasSufficientBudget(long estimatedTokens) => estimatedTokens <= RemainingTokens;

    /// <summary>
    /// Deterministically enforces the hard cut-off: consumes tokens if (and only if)
    /// enough remain, otherwise raises <see cref="BudgetExceededError"/> and leaves
    /// state unchanged.
    /// </summary>
    public void Consume(long tokens)
    {
        if (tokens < 0)
            throw new ArgumentOutOfRangeException(nameof(tokens), "Tokens to consume cannot be negative.");
        if (!HasSufficientBudget(tokens))
            throw new BudgetExceededError(UserId, tokens, RemainingTokens);

        ConsumedTokens += tokens;
    }
    /// <summary>
    /// Reconciles a prior reservation (from HasSufficientBudgetAsync's atomic
    /// check-and-reserve) against actual measured usage - ADR-004(d) step 5. Unlike
    /// Consume, this does NOT throw BudgetExceededError even if it pushes
    /// ConsumedTokens above AllocatedTokens: the tokens were already genuinely spent
    /// with the LLM provider by the time reconciliation runs, so refusing to record
    /// that fact would only make the budget's bookkeeping wrong, not undo the spend.
    /// Going over here is exactly what correctly blocks the user's next
    /// HasSufficientBudget check via a negative RemainingTokens.
    /// </summary>
    public void Reconcile(long actualTokens, long reservedEstimateTokens)
    {
        if (actualTokens < 0)
            throw new ArgumentOutOfRangeException(nameof(actualTokens), "Actual tokens cannot be negative.");
        if (reservedEstimateTokens < 0)
            throw new ArgumentOutOfRangeException(nameof(reservedEstimateTokens), "Reserved estimate tokens cannot be negative.");

        var adjusted = ConsumedTokens - reservedEstimateTokens + actualTokens;
        ConsumedTokens = Math.Max(0, adjusted); // defensive floor only - should not normally trigger
    }
}
