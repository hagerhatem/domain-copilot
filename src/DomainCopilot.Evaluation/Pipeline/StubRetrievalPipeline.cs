using DomainCopilot.Evaluation.Contracts;

namespace DomainCopilot.Evaluation.Pipeline;

/// <summary>
/// Naive lexical-overlap "retrieval" over the crude blurb index in
/// StubCorpusIndex. This is NOT the hybrid dense+keyword retrieval
/// FR-2 requires - it exists only so RetrievalHitRateCalculator has
/// something real to run against before Qdrant + SQL Server full-text
/// search are implemented. Replace with a real IRetrievalPipeline
/// adapter over DomainCopilot.Application/Infrastructure and delete
/// this class and StubCorpusIndex.cs.
/// </summary>
public sealed class StubRetrievalPipeline : IRetrievalPipeline
{
    // The full known corpus filenames, so unscored/unknown-content documents
    // are still candidates (they'll just score near zero via the filename
    // fallback in StubCorpusIndex - that's expected and informative).
    private static readonly string[] AllDocuments =
    {
        "idsa_uti-treatment-women_2010-03_v1.pdf",
        "ispad_pediatric-diabetes-guideline_2022-12_v1.pdf",
        "nice_chronic-heart-failure-management_2018-09_v1.pdf",
        "nice_diabetes-medicines-summary_2026-03_v1.pdf",
        "nice_type2-diabetes-management_2015-12_v1.pdf",
        "who_hypertension-guideline_2021-08_v1.pdf",
        "fda_amlodipine-norvasc-label_v1.pdf",
        "fda_apixaban-eliquis-label_v1.pdf",
        "fda_atorvastatin-lipitor-label_v1.pdf",
        "fda_coumadin-warfarin-label_2016-06_v1.pdf",
        "fda_lisinopril-zestril-label_v1.pdf.pdf",
        "fda_metformin-label_v1.pdf",
        "fda_spironolactone-aldactone-label_v1.pdf",
        "fda_zocor-simvastatin-label_2012-01_v1.pdf",
        "who_essential-medicines-list_2023-07_v23.pdf",
    };

    public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(string question, int topK, CancellationToken ct = default)
    {
        var questionTokens = Tokenize(question);

        var scored = AllDocuments
            .Select(doc => new
            {
                Doc = doc,
                Score = JaccardOverlap(questionTokens, Tokenize(StubCorpusIndex.BlurbOrFallback(doc)))
            })
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => new RetrievedChunk(
                SourceDocument: x.Doc,
                ChunkId: $"{x.Doc}#stub-chunk-0",
                Text: StubCorpusIndex.BlurbOrFallback(x.Doc),
                Score: x.Score))
            .ToList();

        return Task.FromResult<IReadOnlyList<RetrievedChunk>>(scored);
    }

    private static HashSet<string> Tokenize(string text) =>
        text.ToLowerInvariant()
            .Split(new[] { ' ', ',', '.', '(', ')', '/', '-', ':', ';', '?', '\n', '\r', '\t' },
                StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2)
            .ToHashSet();

    private static double JaccardOverlap(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0.0;
        var intersection = a.Intersect(b).Count();
        var union = a.Union(b).Count();
        return union == 0 ? 0.0 : (double)intersection / union;
    }
}
