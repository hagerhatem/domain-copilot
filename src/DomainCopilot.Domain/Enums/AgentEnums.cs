namespace DomainCopilot.Domain.Entities;

/// <summary>The specialized role an <see cref="AgentStep"/> was executed under.</summary>
public enum AgentRole
{
    Orchestrator,
    GuidelineResearcher,
    SafetyChecker,
    DocumentationDrafter
}

/// <summary>Lifecycle status of an individual <see cref="AgentStep"/>.</summary>
public enum AgentStepStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Skipped
}

/// <summary>
/// Lifecycle status of an <see cref="AgentRun"/>.
/// <see cref="Refused"/> is a deliberate, CORRECT terminal outcome — the system lacked
/// sufficient evidence and declined to guess — and is intentionally distinct from
/// <see cref="Failed"/> (an unexpected error) and <see cref="Cancelled"/> (a user- or
/// system-initiated abort). Treating "refused" and "failed" as the same status would
/// hide the one behavior Domain Pack D0 most needs to demonstrate.
/// </summary>
public enum AgentRunStatus
{
    Pending,
    Running,
    AwaitingApproval,
    Completed,
    Rejected,
    Refused,
    /// <summary>
    /// A downstream agent (Safety Checker or Documentation Drafter) failed even
    /// after exhausting retries, but an earlier agent's results were still valid
    /// and are being returned anyway (FR-5's graceful degradation) rather than
    /// discarding a partial answer or crashing the run. Distinct from Failed (an
    /// unrecoverable error with nothing usable to return) and from Completed (the
    /// full pipeline succeeded) - for the same reason Refused is kept distinct from
    /// Failed: collapsing this into either would hide a behavior this system needs
    /// to demonstrate honestly.
    /// </summary>
    Degraded,
    Failed,
    Cancelled
}
