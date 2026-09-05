using DomainCopilot.Application.Orchestration.Ports;
using DomainCopilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DomainCopilot.Infrastructure.Ingestion.Persistence;

public sealed class EfClinicalCaseRepository : IClinicalCaseRepository
{
    private readonly DomainCopilotDbContext _db;

    public EfClinicalCaseRepository(DomainCopilotDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<ClinicalCase?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await _db.ClinicalCases.SingleOrDefaultAsync(c => c.Id == id, ct);

    public async Task AddAsync(ClinicalCase clinicalCase, CancellationToken ct)
    {
        _db.ClinicalCases.Add(clinicalCase);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ClinicalCase>> GetAllByUserAsync(Guid createdByUserId, CancellationToken ct) =>
        await _db.ClinicalCases
            .AsNoTracking()
            .Where(c => c.CreatedByUserId == createdByUserId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);
}