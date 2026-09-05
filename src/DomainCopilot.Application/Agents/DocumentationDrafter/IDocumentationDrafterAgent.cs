namespace DomainCopilot.Application.Agents.DocumentationDrafter;

/// <summary>
/// Drafts the final clinical note from upstream guideline excerpts and safety
/// warnings. Its only tool, DraftClinicalNote, is FR-4's required write/side-effecting
/// tool - the orchestrator must never treat this agent's output as final without
/// routing it through the Clinician approval gate first.
/// </summary>
public interface IDocumentationDrafterAgent : IAgent<DocumentationDrafterInput, DocumentationDrafterOutput>
{
}
