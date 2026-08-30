namespace DomainCopilot.Infrastructure.Retrieval;

using System.Data;
using DomainCopilot.Application.Retrieval.Ports;
using DomainCopilot.Domain.Common;
using DomainCopilot.Domain.Ingestion;
using DomainCopilot.Domain.Retrieval;
using DomainCopilot.Infrastructure.Ingestion.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Keyword leg of hybrid retrieval. Uses FREETEXTTABLE rather than CONTAINS: FREETEXTTABLE
/// treats the input as natural-language text and does its own linguistic stemming/matching,
/// so the caller's raw query string never needs boolean-operator escaping — CONTAINS syntax
/// (quotes, AND/OR/NEAR, wildcards) would otherwise be a second injection surface distinct
/// from SQL injection itself, since a user's question could accidentally (or deliberately)
/// contain characters meaningful to CONTAINS. The query text is still passed as a bound
/// SqlParameter, so it is not concatenated into the SQL text either way.
///
/// Requires a full-text catalog + full-text index on DocumentChunks(Text) — see
/// SqlFullTextIndexInitializer, which must run before this service is queried against a
/// freshly created database.
/// </summary>
public sealed class SqlServerKeywordSearchService : IKeywordSearchService
{
    private readonly DomainCopilotDbContext _db;

    public SqlServerKeywordSearchService(DomainCopilotDbContext db)
    {
        _db = db;
    }

    public async Task<Result<IReadOnlyList<KeywordSearchHit>>> SearchAsync(KeywordSearchQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.QueryText))
            return Result<IReadOnlyList<KeywordSearchHit>>.Success(Array.Empty<KeywordSearchHit>());

        const string sql = """
            SELECT TOP (@topK)
                c.Id            AS ChunkId,
                c.DocumentId    AS DocumentId,
                c.DocumentVersion AS DocumentVersion,
                c.Text          AS Text,
                c.Section       AS Section,
                c.PageNumber    AS PageNumber,
                ft.[RANK]       AS Rank
            FROM DocumentChunks c
            INNER JOIN FREETEXTTABLE(DocumentChunks, Text, @query) ft ON c.Id = ft.[KEY]
            INNER JOIN Documents d ON d.Id = c.DocumentId
            WHERE (@guidelineVersionLabel IS NULL OR d.GuidelineVersionLabel = @guidelineVersionLabel)
              AND (@effectiveAfter IS NULL OR d.GuidelineEffectiveDate >= @effectiveAfter)
              AND (@effectiveBefore IS NULL OR d.GuidelineEffectiveDate <= @effectiveBefore)
              AND (@documentId IS NULL OR c.DocumentId = @documentId)
            ORDER BY ft.[RANK] DESC;
            """;

        try
        {
            var connection = _db.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;
            if (shouldClose)
                await connection.OpenAsync(ct);

            try
            {
                await using var command = (SqlCommand)connection.CreateCommand();
                command.CommandText = sql;
                command.CommandType = CommandType.Text;

                command.Parameters.Add(new SqlParameter("@topK", SqlDbType.Int) { Value = query.TopK });
                command.Parameters.Add(new SqlParameter("@query", SqlDbType.NVarChar, -1) { Value = query.QueryText });
                command.Parameters.Add(new SqlParameter("@guidelineVersionLabel", SqlDbType.NVarChar, 128)
                {
                    Value = (object?)query.Filter?.GuidelineVersionLabel ?? DBNull.Value
                });
                command.Parameters.Add(new SqlParameter("@effectiveAfter", SqlDbType.DateTimeOffset)
                {
                    Value = (object?)query.Filter?.EffectiveOnOrAfter ?? DBNull.Value
                });
                command.Parameters.Add(new SqlParameter("@effectiveBefore", SqlDbType.DateTimeOffset)
                {
                    Value = (object?)query.Filter?.EffectiveOnOrBefore ?? DBNull.Value
                });
                command.Parameters.Add(new SqlParameter("@documentId", SqlDbType.UniqueIdentifier)
                {
                    Value = (object?)query.Filter?.DocumentId?.Value ?? DBNull.Value
                });

                var hits = new List<KeywordSearchHit>();

                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    hits.Add(new KeywordSearchHit(
                        ChunkId: new ChunkId(reader.GetGuid(reader.GetOrdinal("ChunkId"))),
                        DocumentId: new DocumentId(reader.GetGuid(reader.GetOrdinal("DocumentId"))),
                        DocumentVersion: reader.GetInt32(reader.GetOrdinal("DocumentVersion")),
                        Text: reader.GetString(reader.GetOrdinal("Text")),
                        Section: reader.IsDBNull(reader.GetOrdinal("Section")) ? null : reader.GetString(reader.GetOrdinal("Section")),
                        PageNumber: reader.IsDBNull(reader.GetOrdinal("PageNumber")) ? null : reader.GetInt32(reader.GetOrdinal("PageNumber")),
                        Rank: reader.GetInt32(reader.GetOrdinal("Rank"))));
                }

                return Result<IReadOnlyList<KeywordSearchHit>>.Success(hits);
            }
            finally
            {
                if (shouldClose)
                    await connection.CloseAsync();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<KeywordSearchHit>>.Failure(new KeywordSearchFailedError($"Keyword search failed: {ex.Message}"));
        }
    }
}
