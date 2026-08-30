using System.Globalization;
using System.Text;

namespace DomainCopilot.Evaluation.Reporting;

public static class MarkdownReportBuilder
{
    public static string Build(
        IReadOnlyList<EvaluationItemResult> results,
        double groundednessPassThreshold,
        bool isStubRun)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Domain Copilot - Evaluation Harness Report");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"Golden set items evaluated: {results.Count}");
        sb.AppendLine($"Groundedness pass threshold: {groundednessPassThreshold:0.00}");
        sb.AppendLine();

        if (isStubRun)
        {
            sb.AppendLine("> **WARNING - STUB PIPELINE RUN.** Retrieval used a naive lexical-overlap placeholder " +
                          "(not the real Qdrant + SQL Server hybrid pipeline) and the workflow used a fixed " +
                          "placeholder response (not the real multi-agent orchestrator). The numbers below reflect " +
                          "the stubs, not the actual system, and must NOT be reported as FR-3 baseline numbers. " +
                          "Re-run with the real pipeline wired in before recording results in docs/EVALUATION.md.");
            sb.AppendLine();
        }

        AppendOverallSummary(sb, results, groundednessPassThreshold);
        AppendCategoryBreakdown(sb, results, groundednessPassThreshold);
        AppendPerItemDetail(sb, results, groundednessPassThreshold);
        AppendHonestInterpretationPrompt(sb, results);

        return sb.ToString();
    }

    private static void AppendOverallSummary(StringBuilder sb, IReadOnlyList<EvaluationItemResult> results, double threshold)
    {
        var n = results.Count;
        var anyHitRate = Pct(results.Count(r => r.RetrievalHitRate.AnyExpectedDocumentHit), n);
        var allHitRate = Pct(results.Count(r => r.RetrievalHitRate.AllExpectedDocumentsHit), n);
        var meanGroundedness = results.Count == 0 ? 0.0 : results.Average(r => r.Groundedness.Score);
        var groundednessPassRate = Pct(results.Count(r => r.Groundedness.Score >= threshold), n);
        var refusalCorrectRate = Pct(results.Count(r => r.RefusalCheck.Correct), n);

        sb.AppendLine("## Overall Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Retrieval hit-rate (any expected doc in top-K) | {anyHitRate} |");
        sb.AppendLine($"| Retrieval hit-rate (all expected docs in top-K) | {allHitRate} |");
        sb.AppendLine($"| Mean groundedness score | {meanGroundedness.ToString("0.00", CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Groundedness pass rate (>= {threshold:0.00}) | {groundednessPassRate} |");
        sb.AppendLine($"| Refusal/hedge/gate correctness | {refusalCorrectRate} |");
        sb.AppendLine();
    }

    private static void AppendCategoryBreakdown(StringBuilder sb, IReadOnlyList<EvaluationItemResult> results, double threshold)
    {
        sb.AppendLine("## Breakdown by Category");
        sb.AppendLine();
        sb.AppendLine("| Category | N | Hit-rate (any) | Mean groundedness | Refusal/hedge/gate correctness |");
        sb.AppendLine("|---|---|---|---|---|");

        foreach (var group in results.GroupBy(r => r.Item.Category).OrderBy(g => g.Key))
        {
            var n = group.Count();
            var anyHitRate = Pct(group.Count(r => r.RetrievalHitRate.AnyExpectedDocumentHit), n);
            var meanGroundedness = group.Average(r => r.Groundedness.Score);
            var refusalCorrectRate = Pct(group.Count(r => r.RefusalCheck.Correct), n);

            sb.AppendLine($"| {group.Key} | {n} | {anyHitRate} | {meanGroundedness.ToString("0.00", CultureInfo.InvariantCulture)} | {refusalCorrectRate} |");
        }

        sb.AppendLine();
    }

    private static void AppendPerItemDetail(StringBuilder sb, IReadOnlyList<EvaluationItemResult> results, double threshold)
    {
        sb.AppendLine("## Per-Item Detail");
        sb.AppendLine();
        sb.AppendLine("| ID | Category | Expected Behavior | Retrieval Hit (any/all) | Groundedness | Refusal/Hedge/Gate | Notes |");
        sb.AppendLine("|---|---|---|---|---|---|---|");

        foreach (var r in results.OrderBy(r => r.Item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var hit = $"{YesNo(r.RetrievalHitRate.AnyExpectedDocumentHit)}/{YesNo(r.RetrievalHitRate.AllExpectedDocumentsHit)}";
            var groundedness = r.Groundedness.Score.ToString("0.00", CultureInfo.InvariantCulture) +
                               (r.Groundedness.Score >= threshold ? " ✅" : " ⚠️");
            var refusalMark = r.RefusalCheck.Correct ? "✅" : "❌";
            var missedDocsNote = r.RetrievalHitRate.MissedDocuments.Count > 0
                ? $"Missed: {string.Join(", ", r.RetrievalHitRate.MissedDocuments)}. "
                : "";
            var notes = EscapePipes($"{missedDocsNote}{r.RefusalCheck.Explanation}");

            sb.AppendLine($"| {r.Item.Id} | {r.Item.Category} | {r.Item.ExpectedBehaviorRaw} | {hit} | {groundedness} | {refusalMark} | {notes} |");
        }

        sb.AppendLine();
    }

    private static void AppendHonestInterpretationPrompt(StringBuilder sb, IReadOnlyList<EvaluationItemResult> results)
    {
        var failedRefusal = results.Where(r => !r.RefusalCheck.Correct).Select(r => r.Item.Id).ToList();
        var missedRetrieval = results.Where(r => !r.RetrievalHitRate.AnyExpectedDocumentHit).Select(r => r.Item.Id).ToList();

        sb.AppendLine("## Honest Interpretation (fill in for docs/EVALUATION.md)");
        sb.AppendLine();
        sb.AppendLine($"- Items that failed refusal/hedge/gate correctness: {(failedRefusal.Count == 0 ? "none" : string.Join(", ", failedRefusal))}");
        sb.AppendLine($"- Items with a full retrieval miss: {(missedRetrieval.Count == 0 ? "none" : string.Join(", ", missedRetrieval))}");
        sb.AppendLine("- TODO: for each failure above, state whether it's a retrieval problem, a prompting problem, " +
                      "a genuine gap in the corpus, or a harness/metric limitation (e.g. the lexical groundedness " +
                      "heuristic under-scoring a well-paraphrased but correct answer). Do not average these away.");
        sb.AppendLine();
    }

    private static string Pct(int count, int total) =>
        total == 0 ? "n/a" : $"{(100.0 * count / total).ToString("0.0", CultureInfo.InvariantCulture)}% ({count}/{total})";

    private static string YesNo(bool value) => value ? "✅" : "❌";

    private static string EscapePipes(string text) => text.Replace("|", "\\|");
}
