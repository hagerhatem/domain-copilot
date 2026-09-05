using DomainCopilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DomainCopilot.Infrastructure.Ingestion.Persistence.Configurations;

public sealed class TokenBudgetConfiguration : IEntityTypeConfiguration<TokenBudget>
{
    public void Configure(EntityTypeBuilder<TokenBudget> builder)
    {
        builder.ToTable("TokenBudgets");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.UserId).IsRequired();
        builder.Property(b => b.PeriodStart).IsRequired();
        builder.Property(b => b.PeriodEnd).IsRequired();
        builder.Property(b => b.AllocatedTokens).IsRequired();
        builder.Property(b => b.ConsumedTokens).IsRequired();

        // Supports the "one active row per user per period" lookup
        // HasSufficientBudgetAsync/RecordUsageAsync/GetStatusAsync all perform
        // (ADR-004(a)).
        builder.HasIndex(b => new { b.UserId, b.PeriodStart, b.PeriodEnd });
    }
}

public sealed class UsageRecordConfiguration : IEntityTypeConfiguration<UsageRecord>
{
    public void Configure(EntityTypeBuilder<UsageRecord> builder)
    {
        builder.ToTable("UsageRecords");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.RunId).IsRequired();
        builder.Property(u => u.StepId);
        builder.Property(u => u.UserId).IsRequired();
        builder.Property(u => u.ModelName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.EstimatedCostUsd).HasColumnType("decimal(18,6)").IsRequired();
        builder.Property(u => u.RecordedAt).IsRequired();

        builder.OwnsOne(u => u.Usage, usage =>
        {
            usage.Property(t => t.PromptTokens).HasColumnName("UsagePromptTokens");
            usage.Property(t => t.CompletionTokens).HasColumnName("UsageCompletionTokens");
        });

        // Supports the spend view's per-user (recency-ordered) and per-run lookups
        // (T3 capability 5: per-user, per-run, per-agent-step spend view).
        builder.HasIndex(u => new { u.UserId, u.RecordedAt });
        builder.HasIndex(u => u.RunId);
    }
}
