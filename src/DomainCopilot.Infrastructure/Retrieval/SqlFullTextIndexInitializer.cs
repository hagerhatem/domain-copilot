namespace DomainCopilot.Infrastructure.Retrieval;

using DomainCopilot.Infrastructure.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Idempotently ensures the full-text catalog and index that SqlServerKeywordSearchService
/// depends on exist. Run once at startup so `docker compose up` brings up a fully working
/// system with no manual DBA step (Section 7).
///
/// ⚠ Assumes the EF-generated primary key constraint on DocumentChunks is named
/// PK_DocumentChunks (EF Core's default convention for a DbSet mapped ToTable("DocumentChunks")
/// with no explicit HasConstraintName override — true today since no migration exists yet in
/// this repo, but re-verify with sp_helpindex if that ever changes).
///
/// ⚠ Fails fast with a clear message if SQL Server's Full-Text Search component isn't
/// installed on the image in use — Developer Edition supports it, but this is worth a
/// one-line check against whatever mssql-server image tag is pinned in docker-compose.yml.
/// </summary>
public sealed class SqlFullTextIndexInitializer
{
    private const string CatalogName = "ClinicalFtCatalog";
    private readonly DomainCopilotDbContext _db;
    private readonly ILogger<SqlFullTextIndexInitializer> _logger;

    public SqlFullTextIndexInitializer(DomainCopilotDbContext db, ILogger<SqlFullTextIndexInitializer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task EnsureAsync(CancellationToken ct)
    {
        var installed = await _db.Database
            .SqlQueryRaw<int>("SELECT CAST(SERVERPROPERTY('IsFullTextInstalled') AS int) AS Value")
            .FirstOrDefaultAsync(ct);

        if (installed != 1)
        {
            throw new InvalidOperationException(
                "SQL Server Full-Text Search is not installed on this instance. " +
                "Hybrid retrieval's keyword leg cannot function without it — check the " +
                "mssql-server image/edition pinned in docker-compose.yml.");
        }

        await _db.Database.ExecuteSqlRawAsync($"""
            IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = '{CatalogName}')
            BEGIN
                CREATE FULLTEXT CATALOG [{CatalogName}] AS DEFAULT;
            END
            """, ct);

        await _db.Database.ExecuteSqlRawAsync($"""
            IF NOT EXISTS (
                SELECT 1 FROM sys.fulltext_indexes fi
                JOIN sys.tables t ON t.object_id = fi.object_id
                WHERE t.name = 'DocumentChunks'
            )
            BEGIN
                CREATE FULLTEXT INDEX ON DocumentChunks(Text)
                    KEY INDEX PK_DocumentChunks
                    ON [{CatalogName}]
                    WITH CHANGE_TRACKING AUTO;
            END
            """, ct);

        _logger.LogInformation("Full-text catalog/index on DocumentChunks(Text) verified.");
    }
}
