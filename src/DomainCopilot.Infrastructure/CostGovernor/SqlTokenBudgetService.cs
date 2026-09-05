using DomainCopilot.Application.Common;
using DomainCopilot.Application.CostGovernor.Ports;
using DomainCopilot.Domain.Entities;
using DomainCopilot.Domain.Errors;
using DomainCopilot.Domain.ValueObjects;
using DomainCopilot.Infrastructure.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DomainCopilot.Infrastructure.CostGovernor;

/// <summary>
/// Implements ITokenBudgetService exactly per ADR-004(d)'s chosen mechanism:
/// pessimistic row-lock (SELECT ... WITH (UPDLOCK, ROWLOCK)) inside a single
/// transaction, not optimistic concurrency/retry (the ADR's documented alternative,
/// explicitly not chosen as the default). EF Core's LINQ provider cannot express
/// SQL Server table hints, so the lookup uses FromSqlInterpolated with a
/// parameterized raw query - still fully SQL-injection-safe (interpolation holes
/// become real ADO.NET parameters, never string-concatenated).
/// </summary>
public sealed class SqlTokenBudgetService : ITokenBudgetService
{
    private readonly DomainCopilotDbContext _db;
    private readonly IClock _clock;

    public SqlTokenBudgetService(DomainCopilotDbContext db, IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<bool> HasSufficientBudgetAsync(Guid userId, long estimatedTokens, CancellationToken ct)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        if (estimatedTokens < 0)
            throw new ArgumentOutOfRangeException(nameof(estimatedTokens), "Estimated tokens cannot be negative.");

        var now = _clock.UtcNow;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // The row lock acquired here is held until Commit/Rollback below - this is
        // the entire mechanism that makes two concurrent calls for the same userId
        // serialize instead of racing (ADR-004(d) steps 2-3).
        var budget = await _db.TokenBudgets
            .FromSqlInterpolated($@"
                SELECT * FROM TokenBudgets WITH (UPDLOCK, ROWLOCK)
                WHERE UserId = {userId} AND PeriodStart <= {now} AND PeriodEnd > {now}")
            .SingleOrDefaultAsync(ct);

        if (budget is null)
        {
            await tx.RollbackAsync(ct);
            throw new NoActiveBudgetPeriodError(userId, now);
        }

        if (!budget.HasSufficientBudget(estimatedTokens))
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        // Reserves the estimate - safe, HasSufficientBudget was just checked under
        // the row lock, nothing could have changed it since.
        budget.Consume(estimatedTokens);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task RecordUsageAsync(TokenUsageReconciliation reconciliation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reconciliation);

        var now = _clock.UtcNow;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var budget = await _db.TokenBudgets
            .FromSqlInterpolated($@"
                SELECT * FROM TokenBudgets WITH (UPDLOCK, ROWLOCK)
                WHERE UserId = {reconciliation.UserId} AND PeriodStart <= {now} AND PeriodEnd > {now}")
            .SingleOrDefaultAsync(ct);

        if (budget is null)
        {
            await tx.RollbackAsync(ct);
            throw new NoActiveBudgetPeriodError(reconciliation.UserId, now);
        }

        // Deliberately does NOT throw BudgetExceededError even if reconciliation
        // pushes ConsumedTokens above AllocatedTokens - the tokens were already
        // genuinely spent with the LLM provider by the time this runs; refusing to
        // record that fact would make the budget's bookkeeping wrong, not undo the
        // spend. Going over here is exactly what correctly blocks the user's next
        // HasSufficientBudgetAsync call.
        budget.Reconcile(reconciliation.ActualUsage.TotalTokens, reconciliation.ReservedEstimateTokens);

        var usageRecord = UsageRecord.Create(
            reconciliation.RunId,
            reconciliation.UserId,
            reconciliation.ModelName,
            reconciliation.ActualUsage,
            reconciliation.EstimatedCostUsd,
            reconciliation.StepId,
            now);

        await _db.UsageRecords.AddAsync(usageRecord, ct);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task RecordStepUsageAsync(
    Guid userId, Guid runId, Guid stepId, string modelName, TokenUsage usage, decimal estimatedCostUsd, CancellationToken ct)
    {
        var usageRecord = UsageRecord.Create(runId, userId, modelName, usage, estimatedCostUsd, stepId, _clock.UtcNow);
        await _db.UsageRecords.AddAsync(usageRecord, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<TokenBudgetStatus> GetStatusAsync(Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId cannot be empty.", nameof(userId));

        var now = _clock.UtcNow;

        // Plain read, no lock hint - GetStatusAsync is a spend-view query, not a
        // budget-affecting operation, so it doesn't need (and shouldn't take) a
        // row lock that would contend with concurrent HasSufficientBudgetAsync calls.
        var budget = await _db.TokenBudgets
            .SingleOrDefaultAsync(b => b.UserId == userId && b.PeriodStart <= now && b.PeriodEnd > now, ct)
            ?? throw new NoActiveBudgetPeriodError(userId, now);

        var recentUsageEntities = await _db.UsageRecords
            .Where(u => u.UserId == userId)
            .OrderByDescending(u => u.RecordedAt)
            .Take(50)
            .ToListAsync(ct);

        // TotalTokens is a computed property (not a mapped column), so it must be
        // evaluated client-side after materialization, not inside the query above.
        var recentUsage = recentUsageEntities
            .Select(u => new UsageRecordSummary(u.RunId, u.StepId, u.ModelName, u.Usage.TotalTokens, u.EstimatedCostUsd, u.RecordedAt))
            .ToList();

        return new TokenBudgetStatus(
            userId, budget.PeriodStart, budget.PeriodEnd, budget.AllocatedTokens, budget.ConsumedTokens, budget.RemainingTokens, recentUsage);
    }

    public async Task<IReadOnlyList<UsageRecordSummary>> GetUsageByRunAsync(Guid runId, CancellationToken ct)
    {
        var records = await _db.UsageRecords
            .AsNoTracking()
            .Where(u => u.RunId == runId)
            .OrderBy(u => u.RecordedAt)
            .ToListAsync(ct);

        // TotalTokens is a computed property (not a mapped column) - must be
        // evaluated client-side after materialization, same pattern as GetStatusAsync.
        return records
            .Select(u => new UsageRecordSummary(u.RunId, u.StepId, u.ModelName, u.Usage.TotalTokens, u.EstimatedCostUsd, u.RecordedAt))
            .ToList();
    }
}
