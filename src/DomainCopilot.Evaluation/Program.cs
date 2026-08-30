using DomainCopilot.Evaluation.Contracts;
using DomainCopilot.Evaluation.GoldenSet;
using DomainCopilot.Evaluation.Metrics;
using DomainCopilot.Evaluation.Pipeline;
using DomainCopilot.Evaluation.Reporting;

namespace DomainCopilot.Evaluation;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var options = CliOptions.Parse(args);
        if (options is null)
        {
            CliOptions.PrintUsage();
            return 1;
        }

        List<GoldenSetItem> goldenSetItems;
        try
        {
            goldenSetItems = GoldenSetLoader.Load(options.GoldenSetPath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"Loaded {goldenSetItems.Count} golden set items from {options.GoldenSetPath}");

        // --- Manual composition root -------------------------------------------------
        // No DI container package is referenced here on purpose (see .csproj comment).
        // TODO once real pipeline exists: replace these two lines with the real
        // Application-layer adapters (retrieval over Qdrant+SQL, and the orchestrator
        // entry point), each still satisfying IRetrievalPipeline / IWorkflowPipeline.
        // Nothing below this point should need to change when that swap happens.
        IRetrievalPipeline retrievalPipeline = new StubRetrievalPipeline();
        IWorkflowPipeline workflowPipeline = new StubWorkflowPipeline();
        IGroundednessScorer groundednessScorer = new HeuristicGroundednessScorer();
        var usingStubPipeline = true; // flip to false once real pipelines are wired in above
        // -------------------------------------------------------------------------------

        var results = new List<EvaluationItemResult>();

        foreach (var item in goldenSetItems)
        {
            Console.WriteLine($"Running {item.Id} ({item.Category})...");

            var retrievedChunks = await retrievalPipeline.SearchAsync(item.Question, options.TopK);
            var workflowAnswer = await workflowPipeline.RunAsync(item.Question);

            var hitRate = RetrievalHitRateCalculator.Evaluate(item.ExpectedSourceDocuments, retrievedChunks);

            // Groundedness is scored against whatever the workflow itself cited,
            // not against the raw retrieval call above - an ungrounded answer that
            // ignores good retrieval is a real (and different) failure mode.
            var groundedness = groundednessScorer.Score(workflowAnswer.AnswerText, workflowAnswer.CitedChunks);

            var refusalCheck = RefusalCorrectnessChecker.Evaluate(item, workflowAnswer);

            results.Add(new EvaluationItemResult
            {
                Item = item,
                RetrievalHitRate = hitRate,
                Groundedness = groundedness,
                RefusalCheck = refusalCheck,
                ActualAnswerText = workflowAnswer.AnswerText
            });
        }

        var report = MarkdownReportBuilder.Build(results, options.GroundednessThreshold, usingStubPipeline);

        var outputDir = Path.GetDirectoryName(Path.GetFullPath(options.OutputPath));
        if (!string.IsNullOrEmpty(outputDir)) Directory.CreateDirectory(outputDir);
        await File.WriteAllTextAsync(options.OutputPath, report);

        Console.WriteLine();
        Console.WriteLine($"Report written to {options.OutputPath}");
        if (usingStubPipeline)
        {
            Console.WriteLine("NOTE: this was a STUB PIPELINE run. See the warning banner at the top of the report.");
        }

        return 0;
    }
}

internal sealed class CliOptions
{
    public required string GoldenSetPath { get; init; }
    public required string OutputPath { get; init; }
    public required int TopK { get; init; }
    public required double GroundednessThreshold { get; init; }

    public static CliOptions? Parse(string[] args)
    {
        string? goldenSetPath = null;
        string outputPath = "eval/report.md";
        var topK = 5;
        var groundednessThreshold = 0.30;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--golden-set" when i + 1 < args.Length:
                    goldenSetPath = args[++i];
                    break;
                case "--output" when i + 1 < args.Length:
                    outputPath = args[++i];
                    break;
                case "--top-k" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out topK) || topK <= 0)
                    {
                        Console.Error.WriteLine("ERROR: --top-k must be a positive integer.");
                        return null;
                    }
                    break;
                case "--groundedness-threshold" when i + 1 < args.Length:
                    if (!double.TryParse(args[++i], out groundednessThreshold))
                    {
                        Console.Error.WriteLine("ERROR: --groundedness-threshold must be a number.");
                        return null;
                    }
                    break;
                case "--help":
                case "-h":
                    return null;
                default:
                    Console.Error.WriteLine($"ERROR: unrecognized argument '{args[i]}'.");
                    return null;
            }
        }

        if (goldenSetPath is null)
        {
            Console.Error.WriteLine("ERROR: --golden-set <path> is required.");
            return null;
        }

        return new CliOptions
        {
            GoldenSetPath = goldenSetPath,
            OutputPath = outputPath,
            TopK = topK,
            GroundednessThreshold = groundednessThreshold
        };
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
            DomainCopilot.Evaluation - FR-3 evaluation harness

            Usage:
              dotnet run --project DomainCopilot.Evaluation -- --golden-set <path> [options]

            Required:
              --golden-set <path>              Path to golden-set.json

            Options:
              --output <path>                  Markdown report output path (default: eval/report.md)
              --top-k <int>                     Retrieval top-K (default: 5)
              --groundedness-threshold <double> Pass threshold for groundedness score (default: 0.30)

            Example:
              dotnet run --project DomainCopilot.Evaluation -- --golden-set ../eval/golden-set.json --output ../eval/report.md
            """);
    }
}
