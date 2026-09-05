using Microsoft.Extensions.Diagnostics.HealthChecks;
using Qdrant.Client;

namespace DomainCopilot.Api.HealthChecks;

/// <summary>
/// Prompt 11.3: verifies Qdrant is reachable using the SAME QdrantClient instance
/// already registered for retrieval (Program.cs) - no separate connection is opened
/// just for this check. ListCollectionsAsync is a lightweight call that succeeds as
/// long as the gRPC connection and auth (if any) are valid; it does not verify the
/// specific collection (QdrantOptions.CollectionName) exists, since a missing
/// collection is an ingestion/setup concern, not a "is Qdrant reachable" concern.
/// </summary>
public sealed class QdrantHealthCheck : IHealthCheck
{
    private readonly QdrantClient _client;

    public QdrantHealthCheck(QdrantClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.ListCollectionsAsync(cancellationToken);
            return HealthCheckResult.Healthy("Qdrant is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Qdrant is not reachable.", ex);
        }
    }
}