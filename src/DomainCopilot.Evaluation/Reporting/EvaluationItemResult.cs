using DomainCopilot.Evaluation.GoldenSet;
using DomainCopilot.Evaluation.Metrics;

namespace DomainCopilot.Evaluation.Reporting;

public sealed class EvaluationItemResult
{
    public required GoldenSetItem Item { get; init; }
    public required RetrievalHitRateResult RetrievalHitRate { get; init; }
    public required GroundednessResult Groundedness { get; init; }
    public required RefusalCheckResult RefusalCheck { get; init; }
    public required string ActualAnswerText { get; init; }
}
