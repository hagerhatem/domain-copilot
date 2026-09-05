using DomainCopilot.Application.Orchestration.Ports;
using DomainCopilot.Infrastructure.Ingestion.Persistence;

namespace DomainCopilot.Infrastructure.Orchestration;

/// <summary>
/// Deliberately does NOT call SaveChangesAsync itself - it only tracks the new
/// ApprovalAuditEntity on the shared DbContext. ApprovalWorkflowUseCase calls
/// SaveChangesAsync exactly once, after both the AgentRun/ApprovalDecision mutation
/// and this audit entry have been queued, so the two are persisted atomically in one
/// transaction. Calling SaveChangesAsync here too would create a window where the
/// approval decision could be committed without its audit record (or vice versa) if
/// the second call failed - unacceptable for a "fully audited" requirement.
/// </summary>
public sealed class EfApprovalAuditWriter : IApprovalAuditWriter
{
    private readonly DomainCopilotDbContext _db;

    public EfApprovalAuditWriter(DomainCopilotDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task RecordAsync(ApprovalAuditEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var record = new ApprovalAuditEntity
        {
            Id = Guid.NewGuid(),
            RunId = entry.RunId,
            ClinicianUserId = entry.ClinicianUserId,
            PreviousStatus = entry.PreviousStatus,
            DecisionType = entry.DecisionType,
            Comment = entry.Comment,
            DecidedAtUtc = entry.DecidedAtUtc
        };

        await _db.ApprovalAudits.AddAsync(record, ct);
        // No SaveChangesAsync here - see class XML doc.
    }
}
