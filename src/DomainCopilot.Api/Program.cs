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

var builder = WebApplication.CreateBuilder(args);

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

// ---------------------------------------------------------------------------
// SQL Server / EF Core (relational store — Document + DocumentChunk metadata)
// ---------------------------------------------------------------------------
builder.Services.AddDbContext<DomainCopilotDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("SqlServer")));

builder.Services.AddScoped<IDocumentRepository, EfDocumentRepository>();

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

// TODO: register the real IEmbeddingService implementation(s) here once specced —
// e.g. AddScoped<IEmbeddingService, OllamaEmbeddingService>() or a hosted free-tier
// adapter, selected via configuration per Section 3's fallback-chain requirement.
// ⚠ RrfHybridRetrievalService (registered below) depends on IEmbeddingService — until
// this TODO is resolved, resolving IHybridRetrievalService will throw at runtime
// (DI registration itself still succeeds; only actual resolution fails). This is a
// pre-existing gap from the ingestion side, not something introduced by retrieval.

builder.Services.AddScoped<IngestDocumentUseCase>();

// ---------------------------------------------------------------------------
// Retrieval (FR-2: hybrid dense + keyword search, fused via RRF)
// ---------------------------------------------------------------------------
builder.Services.AddScoped<IKeywordSearchService, SqlServerKeywordSearchService>();
builder.Services.AddScoped<IHybridRetrievalService, RrfHybridRetrievalService>();
builder.Services.AddScoped<SqlFullTextIndexInitializer>();

var app = builder.Build();

// ---------------------------------------------------------------------------
// Configure the HTTP request pipeline.
// ---------------------------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// ---------------------------------------------------------------------------
// Startup initializers (run once, so `docker compose up` brings up a fully working
// system with no manual DBA step — Section 7)
// ---------------------------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
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
// Health endpoints (FR-9: health/readiness)
// ---------------------------------------------------------------------------
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (DomainCopilotDbContext db, CancellationToken ct) =>
{
    var canConnect = await db.Database.CanConnectAsync(ct);
    return canConnect ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503);
});

app.Run();