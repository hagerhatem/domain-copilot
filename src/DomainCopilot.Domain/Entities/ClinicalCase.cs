using DomainCopilot.Domain.Common;

namespace DomainCopilot.Domain.Entities;

/// <summary>
/// A fully synthetic patient case that seeds an <see cref="AgentRun"/>: a presenting
/// complaint, relevant (synthetic) patient context, the medication being proposed
/// (checked for interactions/contraindications against current medications), and the
/// patient's current medications list.
///
/// Invariants:
/// - <see cref="IsSyntheticData"/> is always <c>true</c>. The factory refuses to create
///   a case flagged otherwise — real patient data must never enter this system under
///   any circumstances (project brief, Domain Pack D0 corpus requirements: this alone
///   would invalidate the whole submission).
/// - <see cref="PresentingComplaint"/>, <see cref="CaseReference"/>, and
///   <see cref="ProposedMedication"/> are always non-empty.
/// - <see cref="Medications"/> (current medications, distinct from
///   <see cref="ProposedMedication"/>) can only change through
///   <see cref="AddMedication"/> / <see cref="RemoveMedication"/>, which keep the list
///   trimmed and de-duplicated case-insensitively.
/// </summary>
public sealed class ClinicalCase : Entity
{
    private readonly List<string> _medications = new();

    public string CaseReference { get; private set; } = string.Empty;
    public string PresentingComplaint { get; private set; } = string.Empty;
    public string ProposedMedication { get; private set; } = string.Empty;
    public string? PatientContext { get; private set; }
    public bool IsSyntheticData { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<string> Medications => _medications.AsReadOnly();

    private ClinicalCase()
    {
    }

    private ClinicalCase(
        Guid id,
        string caseReference,
        string presentingComplaint,
        string proposedMedication,
        string? patientContext,
        Guid createdByUserId,
        DateTimeOffset createdAt) : base(id)
    {
        CaseReference = caseReference;
        PresentingComplaint = presentingComplaint;
        ProposedMedication = proposedMedication;
        PatientContext = patientContext;
        IsSyntheticData = true;
        CreatedByUserId = createdByUserId;
        CreatedAt = createdAt;
    }

    public static ClinicalCase Create(
        string caseReference,
        string presentingComplaint,
        string proposedMedication,
        Guid createdByUserId,
        string? patientContext = null,
        bool isSyntheticData = true,
        DateTimeOffset? createdAt = null)
    {
        if (string.IsNullOrWhiteSpace(caseReference))
            throw new ArgumentException("Case reference cannot be empty.", nameof(caseReference));
        if (string.IsNullOrWhiteSpace(presentingComplaint))
            throw new ArgumentException("Presenting complaint cannot be empty.", nameof(presentingComplaint));
        if (string.IsNullOrWhiteSpace(proposedMedication))
            throw new ArgumentException("Proposed medication cannot be empty.", nameof(proposedMedication));
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("CreatedByUserId cannot be empty.", nameof(createdByUserId));
        if (!isSyntheticData)
        {
            throw new ArgumentException(
                "Real patient data is never permitted. All clinical cases in this system must be synthetic.",
                nameof(isSyntheticData));
        }

        return new ClinicalCase(
            Guid.NewGuid(),
            caseReference.Trim(),
            presentingComplaint.Trim(),
            proposedMedication.Trim(),
            patientContext?.Trim(),
            createdByUserId,
            createdAt ?? DateTimeOffset.UtcNow);
    }

    public void AddMedication(string medication)
    {
        if (string.IsNullOrWhiteSpace(medication))
            throw new ArgumentException("Medication name cannot be empty.", nameof(medication));

        var normalized = medication.Trim();
        if (!_medications.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            _medications.Add(normalized);
    }

    public void RemoveMedication(string medication)
    {
        _medications.RemoveAll(m => string.Equals(m, medication, StringComparison.OrdinalIgnoreCase));
    }
}