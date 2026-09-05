namespace DomainCopilot.Domain.Errors;

/// <summary>
/// Raised when a token-budget operation targets a user with no TokenBudget row
/// covering the current moment (e.g. a new user with no budget provisioned yet, or
/// a period that expired with no successor row created). Distinct from
/// BudgetExceededError: this means "we don't know this user's budget at all" versus
/// "we know it and it's exhausted" - conflating the two would make an operational
/// gap (missing period provisioning) look identical to a legitimate hard cut-off.
/// </summary>
public sealed class NoActiveBudgetPeriodError : DomainError
{
    public Guid UserId { get; }
    public DateTimeOffset AsOf { get; }

    public NoActiveBudgetPeriodError(Guid userId, DateTimeOffset asOf)
        : base("NO_ACTIVE_BUDGET_PERIOD", $"User {userId} has no active TokenBudget period covering {asOf:O}.")
    {
        UserId = userId;
        AsOf = asOf;
    }
}
