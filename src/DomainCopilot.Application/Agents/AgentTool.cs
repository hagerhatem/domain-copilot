namespace DomainCopilot.Application.Agents;

/// <summary>
/// The complete, closed set of tools any agent in the D0T3 pipeline may invoke.
/// FR-4 requires >= 4 tools total with >= 1 write/side-effecting tool that is never
/// executed without passing the human approval gate. That gating is NOT enforced here
/// - IAgent/AgentContext have no way to force it - it is enforced by the orchestrator
/// only ever calling DocumentationDrafterAgent's write path through
/// DomainCopilot.Domain.Entities.AgentRun's existing state machine
/// (RequestApproval -> ApplyApprovalDecision). This enum only fixes *which* tools
/// exist and lets each agent declare which subset of them it is allowed to use.
///
/// Being a closed enum (not a string) means adding a tool is a deliberate, visible
/// change to this file and to whichever agents' AllowedTools lists are updated - not
/// something that can silently drift via typos in scattered string literals.
/// </summary>
public enum AgentTool
{
    /// <summary>Hybrid (dense + keyword) search over the ingested corpus. Read-only.</summary>
    SearchCorpus,

    /// <summary>Fetch a specific document by id, e.g. to pull more context around a chunk already found via SearchCorpus. Read-only.</summary>
    FetchDocumentById,

    /// <summary>Deterministic, code-based drug interaction/contraindication lookup. Never an LLM guess. Read-only.</summary>
    LookupDrugInteraction,

    /// <summary>
    /// Produces a clinical note draft. This is FR-4's required write/side-effecting
    /// tool: its output must never be finalized without passing the Clinician
    /// approval gate (DomainCopilot.Domain.Entities.AgentRun.RequestApproval /
    /// ApplyApprovalDecision), regardless of any instruction encountered in
    /// retrieved or ingested content (see case_syn-015's prompt-injection scenario).
    /// </summary>
    DraftClinicalNote
}
