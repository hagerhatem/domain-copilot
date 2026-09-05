using DomainCopilot.Application.Ingestion;
using DomainCopilot.Application.Ingestion.Ports;
using DomainCopilot.Application.Retrieval.Ports;
using DomainCopilot.Infrastructure.Ingestion.Chunking;
using DomainCopilot.Infrastructure.Ingestion.Cleaning;
using DomainCopilot.Infrastructure.Ingestion.Extraction;
using DomainCopilot.Infrastructure.Ingestion.Persistence;
using DomainCopilot.Infrastructure.Ingestion.VectorStore;
using DomainCopilot.Infrastructure.Retrieval;
using Microsoft.EntityFrameworkCore;
using Qdrant.Client;
using DomainCopilot.Application.Agents.GuidelineResearcher;
using DomainCopilot.Application.Agents.SafetyChecker;
using DomainCopilot.Application.Agents.DocumentationDrafter;
using DomainCopilot.Application.Llm;
using DomainCopilot.Application.Retrieval;
using DomainCopilot.Infrastructure.Agents;
using DomainCopilot.Infrastructure.Llm;
using DomainCopilot.Infrastructure.Safety;
using DomainCopilot.Infrastructure.Ingestion.Embedding;
using DomainCopilot.Application.Orchestration;
using DomainCopilot.Application.Orchestration.Ports;
using DomainCopilot.Infrastructure.Orchestration;
using DomainCopilot.Application.Common;
using DomainCopilot.Infrastructure.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DomainCopilot.Application.CostGovernor.Ports;
using DomainCopilot.Infrastructure.CostGovernor;
using DomainCopilot.Api.Streaming;
using DomainCopilot.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Serilog;
using DomainCopilot.Api.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using DomainCopilot.Api.Correlation;
using System.Text.Json;


Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .CreateLogger();


var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog();

// ---------------------------------------------------------------------------
// Default ASP.NET Core scaffold services
// ---------------------------------------------------------------------------
builder.Services.AddControllers();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ---------------------------------------------------------------------------
// Options (bound from appsettings.json / appsettings.{Environment}.json / env vars —
// see .env.example for the full list, per Section 2's "no secrets committed" rule)
// ---------------------------------------------------------------------------
builder.Services.Configure<ChunkingOptions>(builder.Configuration.GetSection(ChunkingOptions.SectionName));
builder.Services.Configure<QdrantOptions>(builder.Configuration.GetSection(QdrantOptions.SectionName));
builder.Services.Configure<RrfOptions>(builder.Configuration.GetSection(RrfOptions.SectionName));


builder.Services.AddSingleton<IClock, SystemClock>();

// ---------------------------------------------------------------------------
// SQL Server / EF Core (relational store — Document + DocumentChunk metadata)
// ---------------------------------------------------------------------------
builder.Services.AddDbContext<DomainCopilotDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("SqlServer")));

builder.Services.AddScoped<IDocumentRepository, EfDocumentRepository>();
builder.Services.AddScoped<IDrugInteractionDbContext>(sp => sp.GetRequiredService<DomainCopilotDbContext>());

// ---------------------------------------------------------------------------
// Qdrant (vector store — free, self-hosted via Docker Compose)
// ---------------------------------------------------------------------------
builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<QdrantOptions>>().Value;
    return new QdrantClient(host: opts.Host, port: opts.Port, https: opts.UseTls, apiKey: opts.ApiKey);
});
builder.Services.AddScoped<IVectorStore, QdrantVectorStore>();

// ---------------------------------------------------------------------------
// Ingestion pipeline stages
// ---------------------------------------------------------------------------
// Multiple IDocumentExtractor registrations resolve as IEnumerable<IDocumentExtractor>
// in IngestDocumentUseCase's constructor, keyed internally by DocumentFormat — this is
// exactly the "swap/extend via configuration + one new adapter class" seam Section 3
// requires: adding a new format later means one more AddScoped<IDocumentExtractor, ...>
// line, no changes to Application or Domain.
builder.Services.AddScoped<IDocumentExtractor, PdfPigDocumentExtractor>();
builder.Services.AddScoped<IDocumentExtractor, OpenXmlDocxExtractor>();

builder.Services.AddScoped<IChunkingStrategy, ClinicalSectionAwareChunkingStrategy>();

// STAND-IN — see PlainTextDocumentCleaner's XML doc comment. Replace with the real
// deterministic cleaner (boilerplate stripping, de-hyphenation) when that's specced.
builder.Services.AddScoped<IDocumentCleaner, PlainTextDocumentCleaner>();

// ---------------------------------------------------------------------------
// Embeddings (closes the foundational gap that blocked IHybridRetrievalService and
// both retrieval-dependent agents from resolving at runtime). Uses the same local
// Ollama instance as ILLMProvider but a different model (nomic-embed-text, 768-dim,
// matching QdrantOptions.VectorSize) via its own HttpClient/model pair.
// ---------------------------------------------------------------------------
builder.Services.AddHttpClient<IEmbeddingService, OllamaEmbeddingService>(client =>
{
    var baseUrl = builder.Configuration["Llm:Ollama:BaseUrl"] ?? "http://localhost:11434";
    client.BaseAddress = new Uri(baseUrl);
});

builder.Services.AddScoped<IngestDocumentUseCase>();

// ---------------------------------------------------------------------------
// Retrieval (FR-2: hybrid dense + keyword search, fused via RRF)
// ---------------------------------------------------------------------------
builder.Services.AddScoped<IKeywordSearchService, SqlServerKeywordSearchService>();
builder.Services.AddScoped<IHybridRetrievalService, RrfHybridRetrievalService>();
builder.Services.AddScoped<SqlFullTextIndexInitializer>();

// ---------------------------------------------------------------------------
// LLM (Section 3: ILLMProvider) — Phase 9.2 Cost Governor: CostAwareLlmRouter is
// now the ILLMProvider every agent injects. It wraps two keyed slots, "cheap" and
// "strong". Today both point at the SAME local OllamaLLMProvider (no hosted
// free-tier provider exists yet - see ILLMProvider's XML docs). When one is built,
// only the "strong" registration below changes to point at it; CostAwareLlmRouter
// and every agent stay untouched.
// ---------------------------------------------------------------------------
builder.Services.AddHttpClient("Llm:Ollama:Cheap", client =>
{
    var baseUrl = builder.Configuration["Llm:Ollama:BaseUrl"] ?? "http://localhost:11434";
    client.BaseAddress = new Uri(baseUrl);
});
builder.Services.AddHttpClient("Llm:Ollama:Strong", client =>
{
    var baseUrl = builder.Configuration["Llm:Ollama:BaseUrl"] ?? "http://localhost:11434";
    client.BaseAddress = new Uri(baseUrl);
});

builder.Services.AddKeyedScoped<ILLMProvider>(CostAwareLlmRouter.CheapProviderKey, (sp, _) =>
    new OllamaLLMProvider(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("Llm:Ollama:Cheap"),
        builder.Configuration["Llm:Ollama:CheapModel"] ?? "llama3"));

builder.Services.AddKeyedScoped<ILLMProvider>(CostAwareLlmRouter.StrongProviderKey, (sp, _) =>
    new OllamaLLMProvider(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("Llm:Ollama:Strong"),
        builder.Configuration["Llm:Ollama:StrongModel"] ?? "llama3"));

builder.Services.AddScoped<ILLMProvider>(sp => new CostAwareLlmRouter(
    sp.GetRequiredKeyedService<ILLMProvider>(CostAwareLlmRouter.CheapProviderKey),
    sp.GetRequiredKeyedService<ILLMProvider>(CostAwareLlmRouter.StrongProviderKey)));

builder.Services.AddScoped<ISpendReportService, SqlSpendReportService>();

// ---------------------------------------------------------------------------
// Health checks (Prompt 11.3 / FR-9): verifies SQL Server, Qdrant, and the
// configured LLM provider are reachable. "live" tag = liveness only (process is
// up, no external dependency checks - stays fast/cheap even if a dependency is
// temporarily down). "ready" tag = full readiness (every external dependency
// checked) - what docker-compose's healthcheck directive should poll before
// routing traffic or considering the container "started".
// ---------------------------------------------------------------------------
builder.Services.AddHttpClient(LlmProviderHealthCheck.HttpClientName, client =>
{
    var baseUrl = builder.Configuration["Llm:Ollama:BaseUrl"] ?? "http://localhost:11434";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" })
    .AddSqlServer(
        _ => builder.Configuration.GetConnectionString("SqlServer")
            ?? throw new InvalidOperationException("ConnectionStrings:SqlServer must be configured."),
        name: "sql-server",
        tags: new[] { "ready" })
    .AddCheck<QdrantHealthCheck>("qdrant", tags: new[] { "ready" })
    .AddCheck<LlmProviderHealthCheck>("llm-provider", tags: new[] { "ready" });

// ---------------------------------------------------------------------------
// Multi-Agent System (FR-4)
// ---------------------------------------------------------------------------
builder.Services.Configure<EvidenceSufficiencyOptions>(
    builder.Configuration.GetSection(EvidenceSufficiencyOptions.SectionName));
builder.Services.AddScoped(sp =>
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EvidenceSufficiencyOptions>>().Value);
builder.Services.AddScoped<EvidenceSufficiencyChecker>();

builder.Services.AddScoped<IDrugInteractionLookup, SqlDrugInteractionLookup>();

builder.Services.AddScoped<IGuidelineResearcherAgent, GuidelineResearcherAgent>();
builder.Services.AddScoped<ISafetyCheckerAgent, SafetyCheckerAgent>();
builder.Services.AddScoped<IDocumentationDrafterAgent, DocumentationDrafterAgent>();

// ---------------------------------------------------------------------------
// Orchestrator (FR-5)
// ---------------------------------------------------------------------------
builder.Services.Configure<DomainCopilot.Application.Orchestration.OrchestratorOptions>(
    builder.Configuration.GetSection(DomainCopilot.Application.Orchestration.OrchestratorOptions.SectionName));
builder.Services.AddScoped(sp =>
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DomainCopilot.Application.Orchestration.OrchestratorOptions>>().Value);

builder.Services.AddScoped<IAgentRunRepository, EfAgentRunRepository>();
builder.Services.AddScoped<PipelineOrchestrator>();

// ---------------------------------------------------------------------------
// Prompt 10.1 (FR-6): request-scoped progress channel shared between
// PipelineOrchestrator (writer, via IAgentProgressReporter) and RunsController
// (reader). Scoped, not Transient/Singleton, so both resolve the same instance
// within one HTTP request.
// ---------------------------------------------------------------------------
builder.Services.AddScoped<ChannelAgentProgressReporter>();
builder.Services.AddScoped<IAgentProgressReporter>(sp => sp.GetRequiredService<ChannelAgentProgressReporter>());

builder.Services.AddScoped<IClinicalCaseRepository, EfClinicalCaseRepository>();

builder.Services.AddSingleton<IRunCancellationRegistry, RunCancellationRegistry>();
// ---------------------------------------------------------------------------
// Twist T3 pre-flight cost estimation + hard cut-off (Prompt 9.3)
// ---------------------------------------------------------------------------
builder.Services.Configure<WorkflowCostEstimationOptions>(
    builder.Configuration.GetSection(WorkflowCostEstimationOptions.SectionName));
builder.Services.AddScoped(sp =>
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WorkflowCostEstimationOptions>>().Value);
builder.Services.AddScoped<RunClinicalWorkflowUseCase>();

//----
builder.Services.AddScoped<ITokenBudgetService, SqlTokenBudgetService>();

// ---------------------------------------------------------------------------
// Approval Gate (FR-5 / Prompt 8.2)
// ---------------------------------------------------------------------------
builder.Services.AddScoped<IApprovalAuditWriter, EfApprovalAuditWriter>();
builder.Services.AddScoped<ApprovalWorkflowUseCase>();

// ---------------------------------------------------------------------------
// Authentication/Authorization (Prompt 11.1 / FR-8 - real Identity, replacing the
// earlier minimal JWT-validation-only scaffold). ASP.NET Core Identity backed by
// DomainCopilotDbContext (now an IdentityDbContext), real password verification via
// UserManager, and real JWT issuance via POST /auth/login below - the old
// Development-only /dev/token endpoint is removed.
// ---------------------------------------------------------------------------
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException(
        "Jwt:Key must be configured (appsettings.Development.json for local dev; " +
        "a real secret store for anything else - never commit a real key, see .env.example).");

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        // Demo password ("Demo#12345") must satisfy these - kept at Identity's
        // sensible defaults rather than loosened for convenience.
        options.Password.RequiredLength = 8;
        options.User.RequireUniqueEmail = true;

        // Prompt 12.1 / OWASP A07: the login endpoint previously called
        // CheckPasswordAsync directly, which never touches Identity's own
        // AccessFailedCount tracking - meaning lockout was completely inert
        // despite these options existing. Now enforced explicitly in the
        // /auth/login handler below via AccessFailedAsync/IsLockedOutAsync.
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<DomainCopilotDbContext>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = false,   // still no multi-tenant/external issuer scenario to validate against
            ValidateAudience = false,
            ValidateLifetime = true,
            RoleClaimType = ClaimTypes.Role // matches the claim type /auth/login issues below
        };
    });

builder.Services.AddAuthorization();

// ---------------------------------------------------------------------------
// Rate limiting (Prompt 12.1 / OWASP A07): a global per-IP limiter as defense in
// depth, plus a much stricter named policy for /auth/login specifically, where
// brute-force/credential-stuffing is the concrete threat.
// ---------------------------------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            }));

    options.AddFixedWindowLimiter("login", limiterOptions =>
    {
        limiterOptions.PermitLimit = 5;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueLimit = 0;
    });
});

// ---------------------------------------------------------------------------
// CORS (Prompt 12.1 / OWASP A05): explicit named policy restricted to the Angular
// frontend's origin(s) from config - never AllowAnyOrigin. No AllowCredentials():
// this API authenticates via a Bearer token in the Authorization header (see
// RunStreamService's fetch calls), never cookies, so the browser never needs to
// send/receive credentials cross-origin.
// ---------------------------------------------------------------------------
const string FrontendCorsPolicy = "FrontendCors";
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? new[] { "http://localhost:4200" };
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseMiddleware<DomainCopilot.Api.Correlation.CorrelationIdMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            var correlationId = context.GetCorrelationId();
            await context.Response.WriteAsync(
                JsonSerializer.Serialize(new { error = "An unexpected error occurred.", correlationId }));
        });
    });
}

// Prompt 12.1 / OWASP A05: baseline security headers absent until now.
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "no-referrer");
    context.Response.Headers.Append("Content-Security-Policy", "default-src 'self'");
    await next();
});

// ---------------------------------------------------------------------------
// Configure the HTTP request pipeline.
// ---------------------------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors(FrontendCorsPolicy);
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();

// ---------------------------------------------------------------------------
// Startup initializers (run once, so `docker compose up` brings up a fully working
// system with no manual DBA step — Section 7)
// ---------------------------------------------------------------------------
// Skipped in the "Testing" environment: WebApplicationFactory<Program>-based
// integration tests (e.g. AuthorizationTests) run against a plain mssql
// testcontainer with no Full-Text Search feature installed, and tests unrelated
// to hybrid retrieval shouldn't need it. IngestionPipelineTests/RealEmbeddingServiceTests
// (which DO need this) run the real app pipeline directly, not through
// WebApplicationFactory, so they're unaffected.
// Explicit config flag rather than an environment-name check: WebApplicationFactory's
// UseEnvironment (via its DeferredHostBuilder adapter for minimal-hosting Program.cs
// files) does not reliably propagate to app.Environment in this setup - observed
// directly (the environment-name check never actually skipped this block under
// WebApplicationFactory in testing, despite UseEnvironment("Testing") being set).
// ConfigureAppConfiguration, by contrast, is the one WebApplicationFactory
// customization guaranteed to apply - so integration tests that don't need
// Full-Text Search set this key explicitly instead.
if (!builder.Configuration.GetValue<bool>("SkipStartupInitializers"))
{
    using var scope = app.Services.CreateScope();
    // Ensures the full-text catalog/index that the keyword-search leg of hybrid
    // retrieval (FR-2) depends on exists before any request can hit it.
    // ⚠ Assumes EF migrations (creating the DocumentChunks table itself) already ran
    // by this point. If migrations aren't applied automatically elsewhere in this
    // file yet, this call will fail — add `await db.Database.MigrateAsync();` above
    // it once migrations exist.
    await scope.ServiceProvider
        .GetRequiredService<SqlFullTextIndexInitializer>()
        .EnsureAsync(CancellationToken.None);
}

// ---------------------------------------------------------------------------
// Health endpoints (FR-9 / Prompt 11.3): replaces the earlier minimal hand-rolled
// versions with the real ASP.NET Core health checks framework, filtered by tag.
// Response shape: { "status": "Healthy"|"Degraded"|"Unhealthy", "checks": [...] }.
// ---------------------------------------------------------------------------
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = HealthCheckResponseWriter.WriteJsonAsync
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthCheckResponseWriter.WriteJsonAsync
});

// ---------------------------------------------------------------------------
// Real login endpoint (Prompt 11.1 / FR-8), replacing the old Development-only
// /dev/token stand-in. Available in every environment - this is the actual
// authentication mechanism now, not a test shim.
// ---------------------------------------------------------------------------
app.MapPost("/auth/login", async (LoginRequest request, UserManager<ApplicationUser> userManager) =>
{
    var user = await userManager.FindByEmailAsync(request.Email);

    // Same generic 401 for "no such user", "locked out", and "wrong password" -
    // avoids user-enumeration via response differences (OWASP A07).
    if (user is null)
        return Results.Unauthorized();

    if (await userManager.IsLockedOutAsync(user))
        return Results.Unauthorized();

    if (!await userManager.CheckPasswordAsync(user, request.Password))
    {
        await userManager.AccessFailedAsync(user); // now actually wired to Lockout.MaxFailedAccessAttempts above
        return Results.Unauthorized();
    }

    await userManager.ResetAccessFailedCountAsync(user);

    var roles = await userManager.GetRolesAsync(user);

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new(ClaimTypes.Email, user.Email!)
    };
    claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddHours(1), signingCredentials: creds);

    return Results.Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token) });
})
.RequireRateLimiting("login");



app.Run();
public sealed record LoginRequest(string Email, string Password);

/// <summary>Exposes Program to WebApplicationFactory&lt;Program&gt; in integration tests (top-level statements otherwise generate an internal Program class).</summary>
public partial class Program { }
