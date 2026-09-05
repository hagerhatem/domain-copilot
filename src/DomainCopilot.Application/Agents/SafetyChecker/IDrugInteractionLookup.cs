using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainCopilot.Application.Agents.SafetyChecker;

public sealed record DrugInteractionFinding(string Description, SafetyWarningSeverity Severity, string RuleId);

public sealed record DrugInteractionLookupResult(bool ProposedMedicationRecognized, IReadOnlyList<DrugInteractionFinding> Findings);

/// <summary>
/// Deterministic, code-based drug interaction/contraindication lookup - NEVER an LLM
/// call. See DomainCopilot.Infrastructure.Safety.SqlDrugInteractionLookup for the
/// concrete SQL-Server-backed implementation.
/// </summary>
public interface IDrugInteractionLookup
{
    DrugInteractionLookupResult Check(string proposedMedication, IReadOnlyList<string> currentMedications);
}
