using DomainCopilot.Evaluation.Contracts;

namespace DomainCopilot.Evaluation.Pipeline;

/// <summary>
/// Deliberately does nothing intelligent. It never refuses, never invokes
/// the approval gate, and returns a fixed placeholder answer regardless of
/// the question. This is intentional: a "smarter" stub that guessed at
/// correct-looking behavior would risk being mistaken for real system
/// performance in the markdown report. This one fails almost every metric
/// loudly and consistently, which is the correct signal until the real
/// multi-agent orchestrator (Guideline Researcher -> Safety Checker ->
/// Documentation Drafter) is implemented.
///
/// Replace with a real IWorkflowPipeline adapter over the orchestrator
/// and delete this class.
/// </summary>
public sealed class StubWorkflowPipeline : IWorkflowPipeline
{
    public Task<WorkflowAnswer> RunAsync(string question, CancellationToken ct = default)
    {
        var answer = new WorkflowAnswer(
            AnswerText: "[STUB PIPELINE] Multi-agent workflow not yet implemented - this is a placeholder response.",
            Refused: false,
            ApprovalGateInvoked: false,
            CitedChunks: Array.Empty<RetrievedChunk>());

        return Task.FromResult(answer);
    }
}
