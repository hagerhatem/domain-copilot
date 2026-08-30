using DomainCopilot.Domain.Common;

namespace DomainCopilot.Domain.ValueObjects;

/// <summary>
/// Immutable prompt/completion token counts for a single LLM call. Introduced as a
/// value object (rather than two loose ints) because both <c>AgentStep</c> and
/// <c>UsageRecord</c> need the same pair of numbers together, and keeping
/// <see cref="TotalTokens"/> as a computed property — never settable independently —
/// guarantees the total used for cost/budget accounting can never drift out of sync
/// with its parts.
///
/// Invariant: neither <see cref="PromptTokens"/> nor <see cref="CompletionTokens"/> may
/// be negative.
/// </summary>
public sealed class TokenUsage : ValueObject
{
    public int PromptTokens { get; }
    public int CompletionTokens { get; }
    public int TotalTokens => PromptTokens + CompletionTokens;

    private TokenUsage(int promptTokens, int completionTokens)
    {
        PromptTokens = promptTokens;
        CompletionTokens = completionTokens;
    }

    public static TokenUsage Create(int promptTokens, int completionTokens)
    {
        if (promptTokens < 0)
            throw new ArgumentOutOfRangeException(nameof(promptTokens), "Prompt tokens cannot be negative.");
        if (completionTokens < 0)
            throw new ArgumentOutOfRangeException(nameof(completionTokens), "Completion tokens cannot be negative.");

        return new TokenUsage(promptTokens, completionTokens);
    }

    public static TokenUsage Zero => new(0, 0);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return PromptTokens;
        yield return CompletionTokens;
    }
}
