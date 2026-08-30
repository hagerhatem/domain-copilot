namespace DomainCopilot.Evaluation.Contracts;

/// <summary>
/// TEMPORARY PORT standing in for the real orchestrator entry point
/// (Guideline Researcher -> Safety Checker -> Documentation Drafter).
/// Same deletion note as IRetrievalPipeline: replace with a real
/// Application-layer port once the multi-agent pipeline exists.
/// </summary>
public interface IWorkflowPipeline
{
    Task<WorkflowAnswer> RunAsync(string question, CancellationToken ct = default);
}
