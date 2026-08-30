namespace DomainCopilot.Domain.Errors;

/// <summary>
/// Base type for all explicitly modeled domain errors: expected, named business-rule
/// violations that Application/Api code is expected to catch and translate into a
/// specific refusal, HTTP response, or UI message — as distinct from unexpected
/// infrastructure failures or programming bugs, which should surface as ordinary
/// framework exceptions instead.
///
/// Each concrete <see cref="DomainError"/> carries a stable <see cref="Code"/> (safe to
/// log, assert against in tests, and expose to clients) alongside the human-readable
/// <see cref="Exception.Message"/>. It deliberately extends <see cref="Exception"/> so
/// an aggregate can raise it synchronously from a guard clause — rather than every
/// domain method needing to return a <c>Result&lt;T&gt;</c> wrapper — but callers should
/// treat these as expected control-flow signals for known cases, not as unexpected
/// errors to be logged at error severity.
/// </summary>
public abstract class DomainError : Exception
{
    /// <summary>Stable, machine-readable error code (e.g. "BUDGET_EXCEEDED").</summary>
    public string Code { get; }

    protected DomainError(string code, string message) : base(message)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Domain error code cannot be empty.", nameof(code));

        Code = code;
    }
}
