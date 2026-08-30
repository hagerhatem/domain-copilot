namespace DomainCopilot.Infrastructure.Retrieval;

/// <summary>
/// Reciprocal Rank Fusion (Cormack, Clarke &amp; Buettcher, SIGIR 2009). Combines two
/// independently-ranked lists using rank position only — deliberately ignoring each
/// list's raw scores, since a Qdrant cosine similarity and a SQL Server FREETEXTTABLE
/// RANK value are on entirely incompatible scales and cannot be blended directly.
/// </summary>
public static class ReciprocalRankFusion
{
    public static Dictionary<Guid, double> Fuse(
        IReadOnlyList<Guid> firstRanked,
        IReadOnlyList<Guid> secondRanked,
        int k)
    {
        var scores = new Dictionary<Guid, double>();

        void Accumulate(IReadOnlyList<Guid> ranked)
        {
            for (var i = 0; i < ranked.Count; i++)
            {
                var rank = i + 1; // RRF is defined over 1-based rank
                var contribution = 1.0 / (k + rank);
                scores[ranked[i]] = scores.GetValueOrDefault(ranked[i]) + contribution;
            }
        }

        Accumulate(firstRanked);
        Accumulate(secondRanked);

        return scores;
    }
}
