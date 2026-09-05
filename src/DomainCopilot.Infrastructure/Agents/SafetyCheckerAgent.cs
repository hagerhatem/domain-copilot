using DomainCopilot.Application.Agents;
using DomainCopilot.Application.Agents.SafetyChecker;
using DomainCopilot.Application.Retrieval.Ports;
using DomainCopilot.Domain.Entities;
using DomainCopilot.Domain.Errors;

namespace DomainCopilot.Infrastructure.Agents;

/// <summary>
/// Concrete ISafetyCheckerAgent implementation (Prompt 7.3). Lives in Infrastructure
/// alongside the other two concrete agents, per that prompt.
///
/// DELIBERATE DEVIATION from Prompt 7.3's "each [agent] using ILLMProvider for the
/// reasoning parts": this constructor takes no ILLMProvider dependency at all, and
/// that is intentional, not an oversight. Prompt 7.2 is explicit and more specific
/// than 7.3's general phrasing: the deterministic drug-interaction check "must never
/// be delegated to the LLM". Routing this agent's core safety verdict through an
/// LLM anywhere in its call path - even just to "help" - would reintroduce exactly
/// the hallucination risk Prompt 7.2 exists to eliminate. Where the two prompts
/// conflict for this specific agent, 7.2's explicit safety requirement wins.
///
/// Termination condition (FR-4/7.3): exactly one deterministic lookup call plus one
/// retrieval call per invocation - no loop, and none needed. The deterministic
/// lookup is authoritative and doesn't benefit from retrying; retrieval failures for
/// the supporting-guidance attachment are swallowed rather than retried (see
/// AttachSupportingGuidance) since that attachment is a quality enhancement, not
/// this agent's core correctness guarantee.
/// </summary>
public sealed class SafetyCheckerAgent : ISafetyCheckerAgent
{
    private const int SupportingGuidanceTopK = 5;

    private readonly IDrugInteractionLookup _drugInteractionLookup;
    private readonly IHybridRetrievalService _retrievalService;

    public AgentRole Role => AgentRole.SafetyChecker;

    public IReadOnlyList<AgentTool> AllowedTools { get; } = new[] { AgentTool.LookupDrugInteraction, AgentTool.SearchCorpus };

    public SafetyCheckerAgent(IDrugInteractionLookup drugInteractionLookup, IHybridRetrievalService retrievalService)
    {
        _drugInteractionLookup = drugInteractionLookup ?? throw new ArgumentNullException(nameof(drugInteractionLookup));
        _retrievalService = retrievalService ?? throw new ArgumentNullException(nameof(retrievalService));
    }

    public async Task<AgentResult<SafetyCheckerOutput>> ExecuteAsync(
        AgentContext context, SafetyCheckerInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        var lookupResult = _drugInteractionLookup.Check(input.ProposedMedication, input.CurrentMedications);

        if (!lookupResult.ProposedMedicationRecognized)
        {
            return AgentResult<SafetyCheckerOutput>.Refused(
                new InsufficientEvidenceError(input.ProposedMedication, InsufficientEvidenceReason.OutOfCorpus));
        }

        var warnings = lookupResult.Findings
            .Select(f => new SafetyWarning(f.Description, f.Severity, confidence: 1.0, SafetyWarningSource.DeterministicLookup, f.RuleId))
            .ToList();

        await AttachSupportingGuidance(input, warnings, cancellationToken);

        return AgentResult<SafetyCheckerOutput>.Success(new SafetyCheckerOutput(warnings));
    }

    private async Task AttachSupportingGuidance(SafetyCheckerInput input, List<SafetyWarning> warnings, CancellationToken ct)
    {
        var searchText = $"{input.ProposedMedication} interaction {string.Join(" ", input.CurrentMedications)}";
        var retrievalResult = await _retrievalService.RetrieveAsync(new RetrievalQuery(searchText, SupportingGuidanceTopK), ct);

        if (retrievalResult.IsFailure)
        {
            // Deliberately swallowed - see class XML doc. Should still be logged
            // (correlation id, FR-9) so silent guidance-attachment failures are
            // visible in traces even though they don't block the run.
            return;
        }

        // Severity here is fixed at Info and never auto-escalated from chunk text -
        // classifying free text as Caution/Contraindicated needs either a real
        // rule-extension engine or an LLM call, and this agent deliberately never
        // makes the latter (see class XML doc). TODO once a safe mechanism exists:
        // detect the case where a RetrievedGuideline finding contradicts a
        // DeterministicLookup finding above - that disagreement is this codebase's
        // documented "conflicting sources" scenario (see EvidenceSufficiencyChecker)
        // and should become a Refused outcome with
        // InsufficientEvidenceReason.ConflictingSources once it can be detected.
        warnings.AddRange(retrievalResult.Value.Select(chunk => new SafetyWarning(
            $"Supporting guidance: {Truncate(chunk.Text, 200)}",
            SafetyWarningSeverity.Info,
            confidence: chunk.FusedScore,
            SafetyWarningSource.RetrievedGuideline,
            chunk.ChunkId.Value.ToString())));
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "...";
}
