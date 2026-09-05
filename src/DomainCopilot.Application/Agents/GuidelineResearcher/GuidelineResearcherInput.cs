namespace DomainCopilot.Application.Agents.GuidelineResearcher;

/// <summary>Input to the Guideline Researcher: a natural-language case summary to find relevant guideline content for.</summary>
public sealed record GuidelineResearcherInput
{
    public string CaseSummary { get; }

    public GuidelineResearcherInput(string caseSummary)
    {
        if (string.IsNullOrWhiteSpace(caseSummary))
            throw new ArgumentException("Case summary cannot be empty.", nameof(caseSummary));

        CaseSummary = caseSummary.Trim();
    }
}
