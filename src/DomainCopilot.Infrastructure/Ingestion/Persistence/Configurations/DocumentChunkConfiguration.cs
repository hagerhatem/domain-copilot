namespace DomainCopilot.Infrastructure.Ingestion.Persistence.Configurations;

using DomainCopilot.Domain.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class DocumentChunkConfiguration : IEntityTypeConfiguration<DocumentChunk>
{
    public void Configure(EntityTypeBuilder<DocumentChunk> builder)
    {
        builder.ToTable("DocumentChunks");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasConversion(id => id.Value, value => new ChunkId(value))
            .ValueGeneratedNever();

        builder.Property(c => c.DocumentId)
            .HasConversion(id => id.Value, value => new DocumentId(value))
            .IsRequired();

        // No navigation property by design — chunks are read via IDocumentRepository
        // methods keyed by DocumentId, not by loading a Document aggregate's
        // collection, keeping the Document aggregate itself lightweight.
        builder.HasIndex(c => c.DocumentId);
        builder.HasIndex(c => new { c.DocumentId, c.DocumentVersion });

        builder.Property(c => c.ChunkIndex).IsRequired();
        builder.Property(c => c.Text).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(c => c.Section).HasMaxLength(256);
        builder.Property(c => c.DocumentVersion).IsRequired();
    }
}
