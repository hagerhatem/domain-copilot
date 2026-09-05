using DomainCopilot.Application.Agents.SafetyChecker;

namespace DomainCopilot.Infrastructure.Safety;

/// <summary>
/// Deterministic, code-based (SQL Server-backed) implementation of
/// IDrugInteractionLookup - this is Prompt 7.2's CheckDrugInteractions tool.
///
/// WHY THIS MUST NEVER BE AN LLM CALL (D0 risk statement - arithmetic/factual
/// hallucination must not occur in safety-critical lookups):
///
/// An LLM produces its statistically most likely completion, not a verified fact
/// lookup. It can state a specific drug interaction, a severity level, or "no
/// interaction found" with complete fluency and confidence regardless of whether
/// that claim is actually true - nothing in how it generates text distinguishes a
/// correct, memorized answer from a fluent confabulation. For a safety-critical
/// binary/discrete check like "does drug A interact with drug B", that is exactly
/// the failure mode Domain Pack D0 names as its central risk: not exotic edge
/// cases, but the model being wrong in a completely ordinary, unremarkable way
/// while sounding exactly as confident as when it is right.
///
/// A SQL lookup against a seeded, versioned, auditable table has none of that
/// failure mode: it either finds a matching row or it does not, that result is
/// reproducible and testable, and every finding traces to a specific RuleId a
/// reviewer can look up. No prompt engineering can give an LLM completion the same
/// guarantee. This is why SafetyCheckerAgent's AllowedTools lists
/// AgentTool.LookupDrugInteraction as a plain deterministic call and why
/// SafetyCheckerAgent's constructor deliberately takes no ILLMProvider dependency at
/// all - the safety-critical verdict must never pass through the LLM, not even
/// indirectly.
///
/// NOTE on synchronicity: Check() is intentionally synchronous, matching
/// IDrugInteractionLookup's contract, to keep the "this is a plain lookup, not an
/// LLM round-trip" distinction visible at the call site. A synchronous EF query does
/// block a thread; given the tiny, indexed reference tables involved this is an
/// acceptable trade-off for now, but should be revisited (make the interface async)
/// if this table grows large enough for query latency to matter.
/// </summary>
public sealed class SqlDrugInteractionLookup : IDrugInteractionLookup
{
    private readonly IDrugInteractionDbContext _dbContext;

    public SqlDrugInteractionLookup(IDrugInteractionDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public DrugInteractionLookupResult Check(string proposedMedication, IReadOnlyList<string> currentMedications)
    {
        if (string.IsNullOrWhiteSpace(proposedMedication))
            throw new ArgumentException("Proposed medication cannot be empty.", nameof(proposedMedication));
        ArgumentNullException.ThrowIfNull(currentMedications);

        var normalizedProposed = Normalize(proposedMedication);

        var isRecognized = _dbContext.KnownMedications.Any(m => m.NameNormalized == normalizedProposed);
        if (!isRecognized)
        {
            return new DrugInteractionLookupResult(ProposedMedicationRecognized: false, Findings: Array.Empty<DrugInteractionFinding>());
        }

        var normalizedCurrent = currentMedications.Select(Normalize).ToList();

        var matchingRules = _dbContext.DrugInteractionRules
            .Where(r =>
                (r.DrugANormalized == normalizedProposed && normalizedCurrent.Contains(r.DrugBNormalized)) ||
                (r.DrugBNormalized == normalizedProposed && normalizedCurrent.Contains(r.DrugANormalized)))
            .ToList();

        var findings = matchingRules
            .Select(r => new DrugInteractionFinding(r.Description, ParseSeverity(r.Severity), r.RuleId))
            .ToList();

        return new DrugInteractionLookupResult(ProposedMedicationRecognized: true, Findings: findings);
    }

    private static string Normalize(string medication) => medication.Trim().ToLowerInvariant();

    private static SafetyWarningSeverity ParseSeverity(string severity) => severity switch
    {
        "Info" => SafetyWarningSeverity.Info,
        "Caution" => SafetyWarningSeverity.Caution,
        "Contraindicated" => SafetyWarningSeverity.Contraindicated,
        _ => throw new InvalidOperationException(
            $"Unrecognized severity '{severity}' in DrugInteractionRules table - data corruption or schema drift.")
    };
}
