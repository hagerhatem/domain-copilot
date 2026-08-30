namespace DomainCopilot.Evaluation.Contracts;

/// <summary>
/// The output of running one golden-set question through the full agentic
/// workflow (Guideline Researcher -> Safety Checker -> Documentation Drafter
/// -> orchestrator). This is the contract the harness needs; it does not care
/// how the answer was produced.
/// </summary>
public sealed record WorkflowAnswer(
    string AnswerText,
    bool Refused,
    bool ApprovalGateInvoked,
    IReadOnlyList<RetrievedChunk> CitedChunks);
