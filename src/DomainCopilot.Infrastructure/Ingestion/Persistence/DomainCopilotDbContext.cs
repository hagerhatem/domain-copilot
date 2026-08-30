namespace DomainCopilot.Infrastructure.Ingestion.Persistence;

using DomainCopilot.Domain.Ingestion;
using DomainCopilot.Infrastructure.Ingestion.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

public sealed class DomainCopilotDbContext : DbContext
{
    public DomainCopilotDbContext(DbContextOptions<DomainCopilotDbContext> options) : base(options)
    {
    }

    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new DocumentConfiguration());
        modelBuilder.ApplyConfiguration(new DocumentChunkConfiguration());
    }
}
