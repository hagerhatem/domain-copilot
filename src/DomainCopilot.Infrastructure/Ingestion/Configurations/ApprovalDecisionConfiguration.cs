using DomainCopilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DomainCopilot.Infrastructure.Ingestion.Persistence.Configurations;

public sealed class ApprovalDecisionConfiguration : IEntityTypeConfiguration<ApprovalDecision>
{
    public void Configure(EntityTypeBuilder<ApprovalDecision> builder)
    {
        builder.ToTable("ApprovalDecisions");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.RunId).IsRequired();
        builder.Property(a => a.ClinicianUserId).IsRequired();
        builder.Property(a => a.DecisionType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.OriginalDraftContent).IsRequired();
        builder.Property(a => a.EditedContent);
        builder.Property(a => a.RejectionReason);
        builder.Property(a => a.DecidedAt).IsRequired();

        // Enforces AgentRun.ApplyApprovalDecision's own invariant ("at most one
        // decision per run") at the database level too, not just in memory.
        builder.HasIndex(a => a.RunId).IsUnique();
    }
}
