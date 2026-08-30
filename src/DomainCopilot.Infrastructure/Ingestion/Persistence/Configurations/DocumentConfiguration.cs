namespace DomainCopilot.Infrastructure.Ingestion.Persistence.Configurations;

using DomainCopilot.Domain.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("Documents");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id)
            .HasConversion(id => id.Value, value => new DocumentId(value))
            .ValueGeneratedNever();

        builder.Property(d => d.SourceKey).HasMaxLength(512).IsRequired();
        builder.HasIndex(d => d.SourceKey).IsUnique();

        builder.Property(d => d.FileName).HasMaxLength(512).IsRequired();
        builder.Property(d => d.ContentHash).HasMaxLength(64).IsRequired();
        builder.Property(d => d.Format).HasConversion<string>().HasMaxLength(32);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(d => d.FailureReason).HasMaxLength(2000);
        builder.Property(d => d.GuidelineVersionLabel).HasMaxLength(128);

        builder.Property(d => d.CreatedAtUtc).IsRequired();
        builder.Property(d => d.UpdatedAtUtc).IsRequired();

        // EF Core materializes via the private parameterless constructor and sets the
        // (private-setter) auto-properties through their backing fields — standard
        // for DDD-style entities, no public setters or EF-specific code needed on
        // the Domain type itself.
    }
}
