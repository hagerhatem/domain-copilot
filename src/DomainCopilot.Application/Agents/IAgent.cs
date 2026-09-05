using DomainCopilot.Domain.Entities;

namespace DomainCopilot.Application.Agents;

/// <summary>
/// Every specialized agent (Guideline Researcher, Safety Checker, Documentation
/// Drafter) implements this via its own narrower marker interface
/// (e.g. IGuidelineResearcherAgent : IAgent&lt;GuidelineResearcherInput, GuidelineResearcherOutput&gt;)
/// so DI registration and Infrastructure adapters have a stable, named type to bind
/// to - satisfying Section 3's acceptance test that swapping an implementation is
/// "only configuration changes plus one new adapter class", never a change to
/// Domain or Application code.
///
/// <see cref="Role"/> reuses <see cref="AgentRole"/> from
/// DomainCopilot.Domain.Entities.AgentEnums.cs directly, rather than defining a
/// parallel enum, since it is the same concept AgentStep already persists.
/// </summary>
public interface IAgent<in TInput, TOutput>
{
    /// <summary>Which specialized role this agent fulfils - reused directly on Domain.Entities.AgentStep.AgentRole when the orchestrator records the step.</summary>
    AgentRole Role { get; }

    /// <summary>
    /// The closed, restricted set of tools this agent may invoke. An orchestrator or
    /// tool-dispatch layer should refuse any tool call from this agent that is not in
    /// this list - this is FR-4/OWASP LLM Top 10's "excessive agency" control.
    /// </summary>
    IReadOnlyList<AgentTool> AllowedTools { get; }

    Task<AgentResult<TOutput>> ExecuteAsync(AgentContext context, TInput input, CancellationToken cancellationToken = default);
}
