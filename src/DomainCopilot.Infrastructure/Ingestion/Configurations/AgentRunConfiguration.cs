using DomainCopilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DomainCopilot.Infrastructure.Ingestion.Persistence.Configurations;

public sealed class AgentRunConfiguration : IEntityTypeConfiguration<AgentRun>
{
    public void Configure(EntityTypeBuilder<AgentRun> builder)
    {
        builder.ToTable("AgentRuns");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.ClinicalCaseId).IsRequired();
        builder.Property(r => r.InitiatedByUserId).IsRequired();
        builder.Property(r => r.CorrelationId).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(r => r.TerminationReason).HasMaxLength(2000);
        builder.Property(r => r.CreatedAt).IsRequired();

        // AgentRun.Steps is exposed as IReadOnlyList<AgentStep> (a read-only view
        // over the private _steps field) so EF must write directly to that field
        // rather than through the property - the standard pattern for mapping
        // encapsulated collections without exposing a public setter.
        builder.HasMany(r => r.Steps)
            .WithOne()
            .HasForeignKey(s => s.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(r => r.Steps)
            .HasField("_steps")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne(r => r.Approval)
            .WithOne()
            .HasForeignKey<ApprovalDecision>(a => a.RunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
