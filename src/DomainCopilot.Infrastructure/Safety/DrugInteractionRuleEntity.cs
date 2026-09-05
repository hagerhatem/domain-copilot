namespace DomainCopilot.Infrastructure.Safety;

/// <summary>
/// EF Core persistence model for the seeded drug-interaction reference table backing
/// CheckDrugInteractions (Prompt 7.2). Deliberately NOT a Domain entity: this is
/// static reference data with no lifecycle/invariants of its own to protect (unlike
/// e.g. AgentRun) - a plain persistence record is the honest shape for it.
/// </summary>
public sealed class DrugInteractionRuleEntity
{
    public int Id { get; set; }
    public string RuleId { get; set; } = string.Empty;
    public string DrugANormalized { get; set; } = string.Empty;
    public string DrugBNormalized { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Stored as a string ("Info" | "Caution" | "Contraindicated") for readability in raw SQL/seed scripts; parsed into SafetyWarningSeverity by SqlDrugInteractionLookup.</summary>
    public string Severity { get; set; } = string.Empty;
}

/// <summary>
/// Which medications this system recognizes at all. A proposed medication not found
/// here must cause SafetyCheckerAgent to refuse (InsufficientEvidenceReason.OutOfCorpus)
/// rather than silently report "no interactions found" - "not in our reference data"
/// and "checked, found nothing" are different claims and must never be conflated.
/// </summary>
public sealed class KnownMedicationEntity
{
    public int Id { get; set; }
    public string NameNormalized { get; set; } = string.Empty;
}
