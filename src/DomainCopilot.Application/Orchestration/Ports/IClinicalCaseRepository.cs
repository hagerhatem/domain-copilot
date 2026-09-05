using DomainCopilot.Domain.Entities;

namespace DomainCopilot.Application.Orchestration.Ports;

public interface IClinicalCaseRepository
{
    Task<ClinicalCase?> GetByIdAsync(Guid id, CancellationToken ct);

    Task AddAsync(ClinicalCase clinicalCase, CancellationToken ct);

    /// <summary>Prompt 12.5.4 follow-up: minimal listing so the frontend can select an existing case instead of pasting a raw id. Ordered most-recently-created first.</summary>
    Task<IReadOnlyList<ClinicalCase>> GetAllByUserAsync(Guid createdByUserId, CancellationToken ct);
}