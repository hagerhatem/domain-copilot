namespace DomainCopilot.Application.Agents.GuidelineResearcher;

/// <summary>
/// Retrieves relevant clinical guidance from the ingested corpus for a given case.
/// Read-only: has no write/side-effecting tool. Per the project brief, its tools are
/// "search corpus, fetch document by id".
/// </summary>
public interface IGuidelineResearcherAgent : IAgent<GuidelineResearcherInput, GuidelineResearcherOutput>
{
}
