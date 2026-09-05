namespace DomainCopilot.Application.Agents.SafetyChecker;

/// <summary>
/// Checks drug interactions/contraindications against a deterministic, code-based
/// lookup (never an LLM guess), cross-referenced against retrieved guideline content.
///
/// ASSUMPTION on AllowedTools: the project brief names this agent's deterministic
/// lookup explicitly but does not name a corpus-search tool for it. SearchCorpus is
/// included here because Section 4 requires checking "against ... a deterministic,
/// code-based lookup ... plus retrieved guidance", and this agent's input contract
/// (SafetyCheckerInput) deliberately does NOT receive GuidelineResearcherAgent's
/// excerpts as input - so retrieving that guidance itself, via its own SearchCorpus
/// call, is how this agent gets it. If a future design decides Safety Checker should
/// instead simply receive GuidelineResearcherOutput as an extra input parameter,
/// SearchCorpus should be removed from here and the input record extended.
/// </summary>
public interface ISafetyCheckerAgent : IAgent<SafetyCheckerInput, SafetyCheckerOutput>
{
}
