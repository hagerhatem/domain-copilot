namespace DomainCopilot.Application.Llm;

public sealed record ChatMessage(string Role, string Content);

public sealed record CompletionRequest(
    IReadOnlyList<ChatMessage> Messages,
    double Temperature = 0.2,
    int? MaxOutputTokens = null,
    TaskComplexity Complexity = TaskComplexity.Moderate);

public sealed record CompletionResponse(string Text, int PromptTokens, int CompletionTokens, string ModelUsed);

public sealed record StreamToken(string TextDelta, bool IsFinal);

public sealed record ToolDefinition(string Name, string Description, string JsonSchema);

public sealed record ToolCallRequest(CompletionRequest Completion, IReadOnlyList<ToolDefinition> Tools);

public sealed record ToolCall(string ToolName, string ArgumentsJson);

public sealed record ToolCallResponse(
    string? Text,
    IReadOnlyList<ToolCall> ToolCalls,
    int PromptTokens,
    int CompletionTokens,
    string ModelUsed);

public sealed record EmbedRequest(IReadOnlyList<string> Texts);

public sealed record EmbedResponse(IReadOnlyList<IReadOnlyList<float>> Vectors, string ModelUsed);

/// <summary>
/// System-wide LLM abstraction (project brief Section 3): swapping the hosted
/// provider, the local Ollama model, or the embedding model must require only
/// configuration changes plus one new adapter class implementing this interface -
/// never a change to Domain or Application code.
///
/// STATUS as of Phase 7: only one Infrastructure implementation exists so far
/// (DomainCopilot.Infrastructure.Llm.OllamaLLMProvider - local, free, no API key).
/// Section 3 requires at least two implementations (hosted free-tier + local Ollama)
/// with a documented fallback chain, selected via configuration. The hosted
/// free-tier provider and the fallback-chain wrapper that picks between them are NOT
/// built yet - that remains open work, tracked separately from what Phase 7's agents
/// needed to become implementable today.
/// </summary>
public interface ILLMProvider
{
    Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default);

    IAsyncEnumerable<StreamToken> StreamAsync(CompletionRequest request, CancellationToken cancellationToken = default);

    Task<ToolCallResponse> CallToolAsync(ToolCallRequest request, CancellationToken cancellationToken = default);

    Task<EmbedResponse> EmbedAsync(EmbedRequest request, CancellationToken cancellationToken = default);
}
