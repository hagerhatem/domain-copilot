using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Net;
using System.Net.Http.Json;
using DomainCopilot.Infrastructure.Identity;
using DomainCopilot.Infrastructure.Ingestion.Persistence;
using DomainCopilot.Integration.Tests.Fixtures;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Testcontainers.MsSql;

namespace DomainCopilot.Integration.Tests;

/// <summary>
/// Prompt 11.1's required proof: a non-Clinician (here, the seeded Admin demo user)
/// gets a genuine HTTP 403 when calling the approve endpoint directly - the full
/// ASP.NET Core middleware pipeline (JWT authentication + [Authorize(Roles =
/// "Clinician")] authorization) runs for real via WebApplicationFactory, not a
/// mocked controller call. There is no separate UI-side check being bypassed here.
/// </summary>
public sealed class AuthorizationTests : IAsyncLifetime
{
    // Deliberately does NOT share IngestionContainersFixture/its SQL container.
    // That fixture's IngestionPipelineTests uses Database.EnsureCreatedAsync()
    // (creates tables directly from the current model, no __EFMigrationsHistory
    // row) rather than MigrateAsync(). On a shared container, once
    // EnsureCreatedAsync() has run, __EFMigrationsHistory is still empty, so a
    // later MigrateAsync() call here tries to apply every migration from scratch
    // and collides with tables that already exist ("already an object named
    // DocumentChunks"). A dedicated container sidesteps that entirely - this test
    // class owns its own database and is the only thing that ever migrates it.
    private readonly MsSqlContainer _sqlContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;


    public async Task InitializeAsync()
    {
        await _sqlContainer.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SqlServer"] = _sqlContainer.GetConnectionString(),
                    ["Jwt:Key"] = "test-only-jwt-key-at-least-32-characters-long!!",
                    ["SkipStartupInitializers"] = "true"
                });
            });
        });

        _client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DomainCopilotDbContext>();
        await db.Database.MigrateAsync(); // applies every migration incl. AddIdentityTables + seeds the demo Clinician/Admin users
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _sqlContainer.DisposeAsync();
    }

    [Fact]
    public async Task ApproveEndpoint_Returns403_ForAuthenticatedNonClinicianUser()
    {
        var loginResponse = await _client.PostAsJsonAsync("/auth/login", new
        {
            Email = "admin@demo.local",
            Password = "Demo#12345"
        });
        loginResponse.EnsureSuccessStatusCode();

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(loginBody?.Token);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginBody!.Token);

        // Any well-formed run id works - [Authorize] runs (and must reject) BEFORE
        // the controller action (and therefore before any not-found lookup) ever
        // executes, so a non-existent run id cannot mask this as a 404 instead.
        var approveResponse = await _client.PostAsync($"/runs/{Guid.NewGuid()}/approve", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, approveResponse.StatusCode);
    }

    [Fact]
    public async Task ApproveEndpoint_Returns401_ForUnauthenticatedRequest()
    {
        var response = await _client.PostAsync($"/runs/{Guid.NewGuid()}/approve", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record LoginResponseDto(string Token);
}