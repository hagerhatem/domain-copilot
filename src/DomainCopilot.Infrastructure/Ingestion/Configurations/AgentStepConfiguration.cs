using DomainCopilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DomainCopilot.Infrastructure.Ingestion.Persistence.Configurations;

public sealed class AgentStepConfiguration : IEntityTypeConfiguration<AgentStep>
{
    public void Configure(EntityTypeBuilder<AgentStep> builder)
    {
        builder.ToTable("AgentSteps");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.RunId).IsRequired();
        builder.Property(s => s.StepIndex).IsRequired();
        builder.Property(s => s.AgentRole).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(s => s.ToolName).HasMaxLength(100);
        builder.Property(s => s.Input).IsRequired();
        builder.Property(s => s.Output);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.ModelUsed).HasMaxLength(100);
        builder.Property(s => s.ErrorMessage);

        // TokenUsage (Domain.Common.ValueObject) has no parameterless constructor -
        // EF Core 8 binds it via its (int promptTokens, int completionTokens)
        // constructor by matching parameter names to property names, which works
        // here since they align exactly. TotalTokens is a computed property, not a
        // column - never mapped, never stored, always derived on read.
        builder.OwnsOne(s => s.Usage, usage =>
        {
            usage.Property(u => u.PromptTokens).HasColumnName("UsagePromptTokens");
            usage.Property(u => u.CompletionTokens).HasColumnName("UsageCompletionTokens");
        });

        builder.HasIndex(s => new { s.RunId, s.StepIndex }).IsUnique();
    }
}
