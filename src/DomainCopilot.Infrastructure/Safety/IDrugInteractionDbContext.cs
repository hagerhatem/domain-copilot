using Microsoft.EntityFrameworkCore;

namespace DomainCopilot.Infrastructure.Safety;

/// <summary>
/// Minimal seam so SqlDrugInteractionLookup doesn't need to reference the project's
/// actual DbContext class by name - no DbContext class was found in this codebase in
/// the sessions leading up to Phase 7 (only SqlServerKeywordSearchService and
/// SqlFullTextIndexInitializer were confirmed to exist under Infrastructure/Retrieval,
/// and neither content was reviewed). Rather than guess a name/namespace and risk a
/// reference that won't compile, have your real DbContext implement this interface -
/// two DbSet properties, each a one-line `=> Set&lt;T&gt;();` - and register it for DI
/// alongside your existing DbContext registration.
/// </summary>
public interface IDrugInteractionDbContext
{
    DbSet<DrugInteractionRuleEntity> DrugInteractionRules { get; }
    DbSet<KnownMedicationEntity> KnownMedications { get; }
}
