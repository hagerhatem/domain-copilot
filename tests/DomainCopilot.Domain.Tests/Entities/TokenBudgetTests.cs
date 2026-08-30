using DomainCopilot.Domain.Entities;
using DomainCopilot.Domain.Errors;
using Xunit;

namespace DomainCopilot.Domain.Tests.Entities;

/// <summary>
/// Covers: (b) a TokenBudget correctly rejects a debit that would exceed its
/// remaining balance, and leaves state unchanged when it does.
///
/// All tests operate purely on in-memory Domain objects — no database, no network.
/// </summary>
public class TokenBudgetTests
{
    private static TokenBudget CreateBudget(long allocatedTokens) =>
        TokenBudget.Create(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), allocatedTokens);

    [Fact]
    public void Consume_MoreThanRemaining_ThrowsBudgetExceededErrorAndLeavesStateUnchanged()
    {
        var budget = CreateBudget(allocatedTokens: 1_000);
        budget.Consume(700); // remaining = 300

        var ex = Assert.Throws<BudgetExceededError>(() => budget.Consume(301));

        Assert.Equal(budget.UserId, ex.UserId);
        Assert.Equal(301, ex.RequestedTokens);
        Assert.Equal(300, ex.RemainingTokens);

        // The rejected debit must not have partially applied — this is the hard cut-off.
        Assert.Equal(700, budget.ConsumedTokens);
        Assert.Equal(300, budget.RemainingTokens);
    }

    [Fact]
    public void Consume_ExactlyRemainingBalance_Succeeds()
    {
        var budget = CreateBudget(allocatedTokens: 500);

        budget.Consume(500);

        Assert.Equal(500, budget.ConsumedTokens);
        Assert.Equal(0, budget.RemainingTokens);
    }

    [Fact]
    public void Consume_WithinRemainingBalance_IncrementsConsumedTokensAcrossMultipleCalls()
    {
        var budget = CreateBudget(allocatedTokens: 1_000);

        budget.Consume(400);
        budget.Consume(200);

        Assert.Equal(600, budget.ConsumedTokens);
        Assert.Equal(400, budget.RemainingTokens);
    }

    [Fact]
    public void HasSufficientBudget_ForPreFlightCheck_DoesNotMutateState()
    {
        var budget = CreateBudget(allocatedTokens: 100);

        var result = budget.HasSufficientBudget(1_000); // far more than available

        Assert.False(result);
        Assert.Equal(0, budget.ConsumedTokens); // a pre-flight check must be side-effect free
    }

    [Fact]
    public void Consume_NegativeTokens_ThrowsArgumentOutOfRangeException()
    {
        var budget = CreateBudget(allocatedTokens: 100);

        Assert.Throws<ArgumentOutOfRangeException>(() => budget.Consume(-1));
    }

    [Fact]
    public void Create_WithPeriodEndNotAfterPeriodStart_ThrowsArgumentException()
    {
        var start = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(() =>
            TokenBudget.Create(Guid.NewGuid(), start, start, allocatedTokens: 100));
    }
}
