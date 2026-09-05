using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DomainCopilot.Application.Llm;

namespace DomainCopilot.Infrastructure.Llm;

/// <summary>
/// Local Ollama-backed ILLMProvider implementation - free, no API key, runs entirely
/// offline (project brief's "no paid subscription, no service that cannot run
/// locally" constraint). This is the FIRST of the two required ILLMProvider
/// implementations (Section 3). See ILLMProvider's XML docs for what's still open.
///
/// Deliberately uses plain HttpClient + System.Text.Json rather than the
/// System.Net.Http.Json convenience package, so this file adds zero new NuGet
/// dependencies beyond what a plain class library already has.
///
/// CallTool is a best-effort, prompt-engineered implementation: Ollama's native
/// tool-calling support varies by model/version, so this asks the model to respond
/// with a specific JSON shape and parses that, rather than relying on a guaranteed
/// structured-output API. If you pick a specific local model with real tool-calling
/// support (e.g. recent Llama 3.1+ via /api/chat's "tools" parameter), prefer that
/// directly instead of this fallback parsing approach.
/// </summary>
public sealed class OllamaLLMProvider : ILLMProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly string _model;

    /// <summary>
    /// The caller is expected to configure httpClient.BaseAddress (e.g.
    /// http://localhost:11434) via DI/configuration, not hardcoded here - keeps this
    /// class swappable per the project's config-driven stack requirement.
    /// </summary>
    public OllamaLLMProvider(HttpClient httpClient, string model = "llama3")
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model cannot be empty.", nameof(model));
        _model = model;
    }

    public async Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = new OllamaGenerateRequest(_model, FlattenMessages(request.Messages), false, new OllamaOptions(request.Temperature));
        var body = await PostAsync<OllamaGenerateResponse>("/api/generate", payload, cancellationToken);

        return new CompletionResponse(body.Response, body.PromptEvalCount ?? 0, body.EvalCount ?? 0, _model);
    }

    public async IAsyncEnumerable<StreamToken> StreamAsync(
        CompletionRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = new OllamaGenerateRequest(_model, FlattenMessages(request.Messages), true, new OllamaOptions(request.Temperature));

        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/generate") { Content = content };
        using var response = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line)) continue;

            var chunk = JsonSerializer.Deserialize<OllamaGenerateResponse>(line, JsonOptions)
                ?? throw new InvalidOperationException("Malformed Ollama stream chunk.");

            yield return new StreamToken(chunk.Response, chunk.Done);
            if (chunk.Done) yield break;
        }
    }

    public async Task<ToolCallResponse> CallToolAsync(ToolCallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var toolDescriptions = string.Join("\n", request.Tools.Select(t => $"- {t.Name}: {t.Description} (args schema: {t.JsonSchema})"));
        var toolInstruction =
            "You may call ONE of the following tools if needed, by responding with ONLY a JSON object of the " +
            "form {\"tool\": \"<name>\", \"arguments\": <object matching that tool's schema>}. If no tool call " +
            $"is needed, respond normally with plain text.\n\nAvailable tools:\n{toolDescriptions}";

        var messages = request.Completion.Messages.Append(new ChatMessage("system", toolInstruction)).ToList();
        var completion = await CompleteAsync(request.Completion with { Messages = messages }, cancellationToken);

        if (TryParseToolCall(completion.Text, out var toolCall))
        {
            // SECURITY (Prompt 12.1 / OWASP LLM08 Excessive Agency, LLM02 Insecure
            // Output Handling): never trust the model's own claim about which tool
            // it wants to call. A prompt-injected or hallucinating model could name
            // ANY string as "tool", not only one actually offered in this request.
            // If the named tool isn't in the declared allow-list, treat the
            // response as plain text instead of a tool call - fail closed, never
            // silently pass an unrecognized tool name up the call chain.
            var isAllowedTool = request.Tools.Any(t => string.Equals(t.Name, toolCall.ToolName, StringComparison.Ordinal));
            if (!isAllowedTool)
            {
                return new ToolCallResponse(
                    completion.Text, Array.Empty<ToolCall>(), completion.PromptTokens, completion.CompletionTokens, _model);
            }

            return new ToolCallResponse(null, new[] { toolCall }, completion.PromptTokens, completion.CompletionTokens, _model);
        }

        return new ToolCallResponse(completion.Text, Array.Empty<ToolCall>(), completion.PromptTokens, completion.CompletionTokens, _model);
    }

    public async Task<EmbedResponse> EmbedAsync(EmbedRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var vectors = new List<IReadOnlyList<float>>(request.Texts.Count);
        foreach (var text in request.Texts)
        {
            var body = await PostAsync<OllamaEmbedResponse>("/api/embeddings", new OllamaEmbedRequest(_model, text), cancellationToken);
            vectors.Add(body.Embedding);
        }

        return new EmbedResponse(vectors, _model);
    }

    private async Task<TResponse> PostAsync<TResponse>(string path, object payload, CancellationToken ct)
    {
        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(path, content, ct);
        response.EnsureSuccessStatusCode();

        var responseText = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<TResponse>(responseText, JsonOptions)
            ?? throw new InvalidOperationException($"Ollama returned an empty/unparseable response body from {path}.");
    }

    private static string FlattenMessages(IReadOnlyList<ChatMessage> messages) =>
        string.Join("\n\n", messages.Select(m => $"[{m.Role}]\n{m.Content}"));

    private static bool TryParseToolCall(string text, out ToolCall toolCall)
    {
        toolCall = default!;
        var trimmed = text.Trim();
        if (!trimmed.StartsWith('{')) return false;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (!doc.RootElement.TryGetProperty("tool", out var toolProp)) return false;

            var argsJson = doc.RootElement.TryGetProperty("arguments", out var argsProp) ? argsProp.GetRawText() : "{}";
            toolCall = new ToolCall(toolProp.GetString() ?? "", argsJson);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record OllamaGenerateRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("options")] OllamaOptions Options);

    private sealed record OllamaOptions([property: JsonPropertyName("temperature")] double Temperature);

    private sealed record OllamaGenerateResponse(
        [property: JsonPropertyName("response")] string Response,
        [property: JsonPropertyName("done")] bool Done,
        [property: JsonPropertyName("prompt_eval_count")] int? PromptEvalCount,
        [property: JsonPropertyName("eval_count")] int? EvalCount);

    private sealed record OllamaEmbedRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt);

    private sealed record OllamaEmbedResponse([property: JsonPropertyName("embedding")] List<float> Embedding);
}
