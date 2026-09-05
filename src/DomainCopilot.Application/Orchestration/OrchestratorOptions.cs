namespace DomainCopilot.Application.Orchestration;

/// <summary>
/// Configurable resilience controls for PipelineOrchestrator (FR-5). Bound from
/// configuration by the composition root (Api), same pattern as
/// EvidenceSufficiencyOptions - a plain POCO here since Application depends only on
/// Domain, with IOptions&lt;T&gt; unwrapped at the DI registration site.
/// </summary>
public sealed class OrchestratorOptions
{
    public const string SectionName = "Orchestration";

    /// <summary>
    /// The "max-iteration breaker per agent" (FR-5): how many times the orchestrator
    /// calls a single agent step (including the first attempt) before giving up and
    /// treating it as a resilience-level failure. This is separate from an agent's
    /// own internal retry logic - e.g. GuidelineResearcherAgent's own 3-attempt
    /// query-reformulation loop is a business-logic retry inside that agent; this
    /// cap governs the orchestrator retrying the agent's entire ExecuteAsync call
    /// after a raw transient failure (timeout, dropped connection), a different and
    /// higher-level concern.
    /// </summary>
    public int MaxAttemptsPerAgent { get; set; } = 3;

    /// <summary>Per-step timeout. If an agent call doesn't complete within this window, it's treated as a transient failure eligible for retry.</summary>
    public TimeSpan PerStepTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Base delay for exponential backoff between retry attempts: attempt N waits InitialRetryBackoff * 2^(N-1).</summary>
    public TimeSpan InitialRetryBackoff { get; set; } = TimeSpan.FromSeconds(2);
}
