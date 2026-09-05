namespace DomainCopilot.Infrastructure.Ingestion.Persistence;

using System.Linq;
using DomainCopilot.Domain.Entities;
using DomainCopilot.Domain.Ingestion;
using DomainCopilot.Infrastructure.Identity;
using DomainCopilot.Infrastructure.Ingestion.Persistence.Configurations;
using DomainCopilot.Infrastructure.Safety;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Now an IdentityDbContext (Prompt 11.1 / FR-8) rather than a plain DbContext -
/// adds AspNetUsers/AspNetRoles/etc. alongside every existing table. Guid keys
/// (IdentityUser&lt;Guid&gt;, IdentityRole&lt;Guid&gt;) match every other id in this
/// codebase (AgentRun.Id, ClinicalCase.Id, ...) rather than Identity's string-key
/// default.
/// </summary>
public sealed class DomainCopilotDbContext
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>, DomainCopilot.Infrastructure.Safety.IDrugInteractionDbContext
{
    public DomainCopilotDbContext(DbContextOptions<DomainCopilotDbContext> options) : base(options)
    {
    }
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<DrugInteractionRuleEntity> DrugInteractionRules => Set<DrugInteractionRuleEntity>();
    public DbSet<KnownMedicationEntity> KnownMedications => Set<KnownMedicationEntity>();
    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();
    public DbSet<AgentStep> AgentSteps => Set<AgentStep>();
    public DbSet<ApprovalDecision> ApprovalDecisions => Set<ApprovalDecision>();
    public DbSet<DomainCopilot.Infrastructure.Orchestration.ApprovalAuditEntity> ApprovalAudits => Set<DomainCopilot.Infrastructure.Orchestration.ApprovalAuditEntity>();
    public DbSet<TokenBudget> TokenBudgets => Set<TokenBudget>();
    public DbSet<UsageRecord> UsageRecords => Set<UsageRecord>();
    public DbSet<ClinicalCase> ClinicalCases => Set<ClinicalCase>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // MUST run first - IdentityDbContext.OnModelCreating establishes the
        // AspNetUsers/AspNetRoles/etc. table mappings that everything else here
        // (including the HasData seeding below) builds on top of.
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new DocumentConfiguration());
        modelBuilder.ApplyConfiguration(new DocumentChunkConfiguration());
        modelBuilder.ApplyConfiguration(new DrugInteractionRuleConfiguration());
        modelBuilder.ApplyConfiguration(new KnownMedicationConfiguration());
        modelBuilder.ApplyConfiguration(new AgentRunConfiguration());
        modelBuilder.ApplyConfiguration(new AgentStepConfiguration());
        modelBuilder.ApplyConfiguration(new ApprovalDecisionConfiguration());
        modelBuilder.ApplyConfiguration(new ApprovalAuditConfiguration());
        modelBuilder.ApplyConfiguration(new TokenBudgetConfiguration());
        modelBuilder.ApplyConfiguration(new UsageRecordConfiguration());
        modelBuilder.ApplyConfiguration(new ClinicalCaseConfiguration());

        // Prompt 11.1: seeded demo roles + users, baked into the migration itself
        // (not runtime seeding) - see DemoUsers' XML docs for why.
        modelBuilder.Entity<IdentityRole<Guid>>().HasData(
    new IdentityRole<Guid> { Id = DomainCopilot.Infrastructure.Identity.Roles.ClinicianRoleId, Name = DomainCopilot.Infrastructure.Identity.Roles.Clinician, NormalizedName = "CLINICIAN" },
    new IdentityRole<Guid> { Id = DomainCopilot.Infrastructure.Identity.Roles.AdminRoleId, Name = DomainCopilot.Infrastructure.Identity.Roles.Admin, NormalizedName = "ADMIN" });

        modelBuilder.Entity<ApplicationUser>().HasData(DemoUsers.BuildClinician(), DemoUsers.BuildAdmin());

        modelBuilder.Entity<IdentityUserRole<Guid>>().HasData(
    new IdentityUserRole<Guid> { UserId = DemoUsers.ClinicianUserId, RoleId = DomainCopilot.Infrastructure.Identity.Roles.ClinicianRoleId },
    new IdentityUserRole<Guid> { UserId = DemoUsers.AdminUserId, RoleId = DomainCopilot.Infrastructure.Identity.Roles.AdminRoleId });
    }
}