namespace DomainCopilot.Domain.Errors;

/// <summary>
/// Raised when consuming (or pre-flight estimating) tokens would push a user's
/// <see cref="Entities.TokenBudget"/> over its allocated ceiling for the current period.
///
/// This is the enforcement mechanism behind the Cost Governor's hard cut-off: it is
/// raised by <see cref="Entities.TokenBudget"/> itself and can never be bypassed by a
/// caller, so "budget exhausted" is a domain invariant — not something the Api layer or
/// a UI merely warns about while letting the run proceed anyway.
/// </summary>
public sealed class BudgetExceededError : DomainError
{
    public Guid UserId { get; }
    public long RequestedTokens { get; }
    public long RemainingTokens { get; }

    public BudgetExceededError(Guid userId, long requestedTokens, long remainingTokens)
        : base("BUDGET_EXCEEDED", BuildMessage(userId, requestedTokens, remainingTokens))
    {
        UserId = userId;
        RequestedTokens = requestedTokens;
        RemainingTokens = remainingTokens;
    }

    private static string BuildMessage(Guid userId, long requestedTokens, long remainingTokens) =>
        $"User {userId} requested {requestedTokens} tokens but only {remainingTokens} remain in the current budget period.";
}
