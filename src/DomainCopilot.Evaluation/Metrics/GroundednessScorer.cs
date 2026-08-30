using DomainCopilot.Evaluation.Contracts;

namespace DomainCopilot.Evaluation.Metrics;

public sealed record GroundednessResult(
    double Score,
    IReadOnlyList<(string Claim, double BestOverlap)> ClaimScores);

/// <summary>
/// Extension point: an LLM-as-judge implementation (calling the real
/// ILLMProvider once it exists) should implement this interface instead of
/// - or alongside - HeuristicGroundednessScorer, and Program.cs can pick
/// whichever is configured. The heuristic below is intentionally crude:
/// it is a floor, not a substitute for a real judge, and FR-3 explicitly
/// allows either approach.
/// </summary>
public interface IGroundednessScorer
{
    GroundednessResult Score(string answerText, IReadOnlyList<RetrievedChunk> citedChunks);
}

/// <summary>
/// Simple claim-to-chunk lexical overlap heuristic:
/// 1. Split the answer into naive "claims" (sentences).
/// 2. For each claim, compute word-overlap against every cited chunk's text.
/// 3. A claim's groundedness is its best (max) overlap against any chunk.
/// 4. The answer's overall score is the mean across claims.
///
/// This will systematically under-score genuinely well-grounded answers
/// that paraphrase heavily (which is expected and good writing), and can
/// over-score answers that just repeat retrieved boilerplate. Treat the
/// resulting number as a rough triage signal, not ground truth - flag any
/// answer below the threshold for manual review rather than trusting the
/// score in isolation.
/// </summary>
public sealed class HeuristicGroundednessScorer : IGroundednessScorer
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "is", "are", "was", "were", "be", "been", "to", "of", "in", "on",
        "for", "and", "or", "with", "as", "at", "by", "this", "that", "it", "its", "if",
        "not", "no", "does", "do", "did", "should", "must", "may", "can", "will", "per"
    };

    public GroundednessResult Score(string answerText, IReadOnlyList<RetrievedChunk> citedChunks)
    {
        var claims = SplitIntoClaims(answerText);
        var chunkTokenSets = citedChunks.Select(c => Tokenize(c.Text)).ToList();

        if (claims.Count == 0 || chunkTokenSets.Count == 0)
        {
            return new GroundednessResult(0.0, Array.Empty<(string, double)>());
        }

        var claimScores = new List<(string Claim, double BestOverlap)>();
        foreach (var claim in claims)
        {
            var claimTokens = Tokenize(claim);
            var best = chunkTokenSets.Count == 0
                ? 0.0
                : chunkTokenSets.Max(chunkTokens => Overlap(claimTokens, chunkTokens));
            claimScores.Add((claim, best));
        }

        var overall = claimScores.Average(c => c.BestOverlap);
        return new GroundednessResult(overall, claimScores);
    }

    private static List<string> SplitIntoClaims(string text) =>
        text.Split(new[] { '.', '!', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

    private static HashSet<string> Tokenize(string text) =>
        text.ToLowerInvariant()
            .Split(new[] { ' ', ',', '.', '(', ')', '/', '-', ':', ';', '?', '\n', '\r', '\t' },
                StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2 && !StopWords.Contains(t))
            .ToHashSet();

    /// <summary>What fraction of the claim's meaningful tokens also appear in the chunk.</summary>
    private static double Overlap(HashSet<string> claimTokens, HashSet<string> chunkTokens)
    {
        if (claimTokens.Count == 0) return 0.0;
        var shared = claimTokens.Intersect(chunkTokens).Count();
        return (double)shared / claimTokens.Count;
    }
}
