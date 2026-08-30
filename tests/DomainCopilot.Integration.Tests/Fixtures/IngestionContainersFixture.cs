namespace DomainCopilot.Integration.Tests.Fixtures;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.MsSql;
using Xunit;

/// <summary>
/// Both containers are free/open-source images pulled from Docker Hub — no paid
/// service, consistent with Section 2's constraint. Requires a running Docker daemon
/// (Docker Desktop locally, or the built-in Docker support on GitHub Actions'
/// ubuntu-latest runners in CI — no extra setup needed there).
/// </summary>
public sealed class IngestionContainersFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    // No dedicated Testcontainers.Qdrant module referenced here — a generic
    // ContainerBuilder keeps this test independent of that package's availability/
    // version in your Testcontainers install. Swap to Testcontainers.Qdrant's
    // QdrantBuilder if you have it and prefer the typed wrapper.
    private readonly IContainer _qdrantContainer = new ContainerBuilder("qdrant/qdrant:v1.11.0")
        .WithPortBinding(6333, true) // REST — used only for the wait strategy below
        .WithPortBinding(6334, true) // gRPC — used by QdrantClient
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(r => r.ForPort(6333).ForPath("/readyz")))
        .Build();

    public string SqlConnectionString => _sqlContainer.GetConnectionString();
    public string QdrantHost => _qdrantContainer.Hostname;
    public int QdrantGrpcPort => _qdrantContainer.GetMappedPublicPort(6334);

    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            _sqlContainer.StartAsync(),
            _qdrantContainer.StartAsync());
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(
            _sqlContainer.DisposeAsync().AsTask(),
            _qdrantContainer.DisposeAsync().AsTask());
    }
}

[CollectionDefinition(Name)]
public sealed class IngestionContainersCollection : ICollectionFixture<IngestionContainersFixture>
{
    public const string Name = "Ingestion containers collection";
}
