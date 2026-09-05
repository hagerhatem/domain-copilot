using DomainCopilot.Application.Agents.GuidelineResearcher;
using DomainCopilot.Application.Agents.SafetyChecker;

namespace DomainCopilot.Application.Agents.DocumentationDrafter;

/// <summary>
/// Input to the Documentation Drafter: what the two upstream agents produced, plus
/// the case narrative needed to actually write a note (see ASSUMPTION below).
///
/// ASSUMPTION: the project brief's Prompt 7.1 describes this agent's input as
/// "guideline excerpts + safety warnings" only. CaseSummary was added because
/// drafting a clinical note without the presenting complaint/case narrative itself
/// is not achievable in practice - the excerpts and warnings are supporting evidence
/// for a note, not the note's subject matter. Flagging this as a deliberate
/// interpretation rather than a silent scope change.
/// </summary>
public sealed record DocumentationDrafterInput
{
    public string CaseSummary { get; }
    public IReadOnlyList<GuidelineExcerpt> GuidelineExcerpts { get; }
    public IReadOnlyList<SafetyWarning> SafetyWarnings { get; }

    public DocumentationDrafterInput(
        string caseSummary,
        IReadOnlyList<GuidelineExcerpt> guidelineExcerpts,
        IReadOnlyList<SafetyWarning> safetyWarnings)
    {
        if (string.IsNullOrWhiteSpace(caseSummary))
            throw new ArgumentException("Case summary cannot be empty.", nameof(caseSummary));
        ArgumentNullException.ThrowIfNull(guidelineExcerpts);
        ArgumentNullException.ThrowIfNull(safetyWarnings);

        CaseSummary = caseSummary.Trim();
        GuidelineExcerpts = guidelineExcerpts;
        SafetyWarnings = safetyWarnings;
    }
}
