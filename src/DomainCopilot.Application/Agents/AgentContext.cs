namespace DomainCopilot.Application.Agents;

/// <summary>
/// The run-scoped context passed into every agent invocation. Deliberately thin: it
/// carries identifiers an agent needs for tracing/auditing (FR-9's correlation id
/// flowing request -> orchestrator -> agent -> LLM call) and to know which
/// <see cref="Domain.Entities.ClinicalCase"/> and
/// <see cref="Domain.Entities.AgentRun"/> it is operating within - not business data
/// itself, which belongs in each agent's strongly-typed TInput instead.
///
/// Deliberately does NOT carry a step index, tool budget, or token budget remaining:
/// those are orchestrator/Cost-Governor bookkeeping concerns (see
/// DomainCopilot.Domain.Entities.AgentRun.AddStep and the eventual TokenBudget
/// integration), not something an individual agent implementation should reach into.
/// </summary>
public sealed class AgentContext
{
    public Guid RunId { get; }
    public Guid ClinicalCaseId { get; }
    public string CorrelationId { get; }

    public AgentContext(Guid runId, Guid clinicalCaseId, string correlationId)
    {
        if (runId == Guid.Empty)
            throw new ArgumentException("RunId cannot be empty.", nameof(runId));
        if (clinicalCaseId == Guid.Empty)
            throw new ArgumentException("ClinicalCaseId cannot be empty.", nameof(clinicalCaseId));
        if (string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("CorrelationId cannot be empty.", nameof(correlationId));

        RunId = runId;
        ClinicalCaseId = clinicalCaseId;
        CorrelationId = correlationId.Trim();
    }
}
