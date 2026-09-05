using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using DomainCopilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DomainCopilot.Infrastructure.Ingestion.Persistence.Configurations;

/// <summary>
/// FIRST use in this codebase of a mapped primitive collection (List&lt;string&gt;)
/// on a private backing field. Unlike AgentRunConfiguration's HasMany (a real
/// relationship to another entity, AgentStep), ClinicalCase's _medications is a
/// plain list of strings with no identity of its own - a separate child table would
/// be overkill. Stored as a single JSON column instead (EF Core value conversion +
/// a value comparer so change-tracking can correctly detect list mutations, which
/// reference equality alone would miss).
/// </summary>
public sealed class ClinicalCaseConfiguration : IEntityTypeConfiguration<ClinicalCase>
{
    public void Configure(EntityTypeBuilder<ClinicalCase> builder)
    {
        builder.ToTable("ClinicalCases");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.CaseReference).HasMaxLength(200).IsRequired();
        builder.Property(c => c.PresentingComplaint).HasMaxLength(4000).IsRequired();
        builder.Property(c => c.ProposedMedication).HasMaxLength(200).IsRequired();
        builder.Property(c => c.PatientContext).HasMaxLength(4000);
        builder.Property(c => c.IsSyntheticData).IsRequired();
        builder.Property(c => c.CreatedByUserId).IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();

        builder.Property<List<string>>("_medications")
            .HasColumnName("Medications")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
            .Metadata.SetValueComparer(new ValueComparer<List<string>>(
                (a, b) => a!.SequenceEqual(b!),
                v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
                v => v.ToList()));
    }
}