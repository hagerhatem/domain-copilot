using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DomainCopilot.Infrastructure.Safety;

public sealed class DrugInteractionRuleConfiguration : IEntityTypeConfiguration<DrugInteractionRuleEntity>
{
    public void Configure(EntityTypeBuilder<DrugInteractionRuleEntity> builder)
    {
        builder.ToTable("DrugInteractionRules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.RuleId).HasMaxLength(100).IsRequired();
        builder.Property(r => r.DrugANormalized).HasMaxLength(200).IsRequired();
        builder.Property(r => r.DrugBNormalized).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(1000).IsRequired();
        builder.Property(r => r.Severity).HasMaxLength(30).IsRequired();
        builder.HasIndex(r => new { r.DrugANormalized, r.DrugBNormalized }).IsUnique();

        builder.HasData(DrugInteractionSeedData.Rules);
    }
}

public sealed class KnownMedicationConfiguration : IEntityTypeConfiguration<KnownMedicationEntity>
{
    public void Configure(EntityTypeBuilder<KnownMedicationEntity> builder)
    {
        builder.ToTable("KnownMedications");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.NameNormalized).HasMaxLength(200).IsRequired();
        builder.HasIndex(m => m.NameNormalized).IsUnique();

        builder.HasData(DrugInteractionSeedData.KnownMedications);
    }
}
