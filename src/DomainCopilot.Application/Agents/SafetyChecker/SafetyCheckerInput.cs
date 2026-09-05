namespace DomainCopilot.Application.Agents.SafetyChecker;

/// <summary>
/// Input to the Safety Checker: a medication being proposed/considered, plus the
/// patient's existing medication list and any other free-text patient context needed
/// to judge interactions/contraindications.
///
/// ASSUMPTION: the project brief's Prompt 7.1 describes this agent's input as
/// "proposed medication + patient context" only. CurrentMedications was added here
/// because a real interaction/contraindication check cannot be performed against a
/// single medication in isolation - it always needs to know what else the patient is
/// taking (see case_syn-004: lisinopril + spironolactone flagged together, not
/// spironolactone alone). Flagging this as a deliberate interpretation rather than a
/// silent scope change.
/// </summary>
public sealed record SafetyCheckerInput
{
    public string ProposedMedication { get; }
    public IReadOnlyList<string> CurrentMedications { get; }
    public string? PatientContext { get; }

    public SafetyCheckerInput(string proposedMedication, IReadOnlyList<string> currentMedications, string? patientContext = null)
    {
        if (string.IsNullOrWhiteSpace(proposedMedication))
            throw new ArgumentException("Proposed medication cannot be empty.", nameof(proposedMedication));
        ArgumentNullException.ThrowIfNull(currentMedications);

        ProposedMedication = proposedMedication.Trim();
        CurrentMedications = currentMedications;
        PatientContext = patientContext?.Trim();
    }
}
