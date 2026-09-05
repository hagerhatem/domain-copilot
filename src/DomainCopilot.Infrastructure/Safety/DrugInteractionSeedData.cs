namespace DomainCopilot.Infrastructure.Safety;

/// <summary>
/// Seed values for the drug-interaction reference tables, applied via EF Core
/// migrations (HasData in DrugInteractionRuleConfiguration/KnownMedicationConfiguration).
/// ILLUSTRATIVE/SYNTHETIC ONLY - matches eval/golden-set.json's known cases so the
/// evaluation harness has something real to exercise, not a comprehensive clinical
/// rule set. Record this as a known, deliberate MVP simplification in
/// docs/SYSTEM-DESIGN.md's gap table, not silently left undocumented. Replace/expand
/// once the drug-reference corpus (FDA labels, WHO essential medicines list) is
/// actually ingested and can drive this from structured data instead.
/// </summary>
public static class DrugInteractionSeedData
{
    public static readonly KnownMedicationEntity[] KnownMedications =
    {
        new() { Id = 1, NameNormalized = "metformin" },
        new() { Id = 2, NameNormalized = "lisinopril" },
        new() { Id = 3, NameNormalized = "spironolactone" },
        new() { Id = 4, NameNormalized = "warfarin" },
        new() { Id = 5, NameNormalized = "atorvastatin" },
        new() { Id = 6, NameNormalized = "simvastatin" },
        new() { Id = 7, NameNormalized = "amlodipine" },
        new() { Id = 8, NameNormalized = "apixaban" },
        new() { Id = 9, NameNormalized = "empagliflozin" },
        new() { Id = 10, NameNormalized = "insulin glargine" },
        new() { Id = 11, NameNormalized = "ciprofloxacin" },
        new() { Id = 12, NameNormalized = "levofloxacin" },
        new() { Id = 13, NameNormalized = "nitrofurantoin" },
        new() { Id = 14, NameNormalized = "trimethoprim-sulfamethoxazole" },
        new() { Id = 15, NameNormalized = "pioglitazone" },
    };

    public static readonly DrugInteractionRuleEntity[] Rules =
    {
        new()
        {
            Id = 1,
            RuleId = "RULE_ACEI_KSPARING_HYPERKALEMIA",
            DrugANormalized = "lisinopril",
            DrugBNormalized = "spironolactone",
            Description = "ACE inhibitor + potassium-sparing diuretic combination carries a hyperkalemia risk; recommend potassium monitoring, especially at reduced renal function.",
            Severity = "Caution"
        },
        new()
        {
            Id = 2,
            RuleId = "RULE_FLUOROQUINOLONE_WARFARIN_BLEEDING",
            DrugANormalized = "warfarin",
            DrugBNormalized = "ciprofloxacin",
            Description = "Fluoroquinolone antibiotics are among the drug classes that can increase bleeding risk in patients on warfarin; recommend closer INR monitoring than the routine schedule.",
            Severity = "Caution"
        },
        new()
        {
            Id = 3,
            RuleId = "RULE_FLUOROQUINOLONE_WARFARIN_BLEEDING",
            DrugANormalized = "warfarin",
            DrugBNormalized = "levofloxacin",
            Description = "Fluoroquinolone antibiotics are among the drug classes that can increase bleeding risk in patients on warfarin; recommend closer INR monitoring than the routine schedule.",
            Severity = "Caution"
        },
    };
}
