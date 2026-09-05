using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DomainCopilot.Api.HealthChecks;

/// <summary>
/// Prompt 11.3: verifies the configured local LLM provider (Ollama) is reachable.
/// Uses a dedicated named HttpClient (registered separately from the "cheap"/
/// "strong" ILLMProvider slots in Program.cs - see CostAwareLlmRouter's docs) so a
/// health check never shares state/lifetime with the actual completion-serving
/// clients. GET /api/tags is Ollama's lightweight "list loaded models" endpoint -
/// this confirms the Ollama server itself is up and responding, NOT that any
/// specific model is loaded or that inference actually works end-to-end.
/// </summary>
public sealed class LlmProviderHealthCheck : IHealthCheck
{
    public const string HttpClientName = "HealthCheck:Ollama";

    private readonly IHttpClientFactory _httpClientFactory;

    public LlmProviderHealthCheck(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var response = await client.GetAsync("/api/tags", cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("LLM provider (Ollama) is reachable.")
                : HealthCheckResult.Unhealthy($"LLM provider (Ollama) returned {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("LLM provider (Ollama) is not reachable.", ex);
        }
    }
}