using DomainCopilot.Infrastructure.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DomainCopilot.Infrastructure.Ingestion.Persistence.Configurations;

public sealed class ApprovalAuditConfiguration : IEntityTypeConfiguration<ApprovalAuditEntity>
{
    public void Configure(EntityTypeBuilder<ApprovalAuditEntity> builder)
    {
        builder.ToTable("ApprovalAudits");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.RunId).IsRequired();
        builder.Property(a => a.ClinicianUserId).IsRequired();
        builder.Property(a => a.PreviousStatus).HasMaxLength(30).IsRequired();
        builder.Property(a => a.DecisionType).HasMaxLength(30).IsRequired();
        builder.Property(a => a.Comment).HasMaxLength(2000);
        builder.Property(a => a.DecidedAtUtc).IsRequired();

        // Query pattern this index supports: "show me every approval action ever
        // taken on this run", the audit trail's primary read access pattern.
        builder.HasIndex(a => a.RunId);
    }
}
