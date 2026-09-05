using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainCopilot.Infrastructure.Ingestion.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

/// <summary>
/// Lets `dotnet ef` create DomainCopilotDbContext directly at design time, bypassing
/// the full ASP.NET Core host/DI container that Program.cs would otherwise require.
/// This is necessary because Program.cs currently has an unregistered dependency
/// (IEmbeddingService - a pre-existing, documented gap from the ingestion side, not
/// something introduced by Phase 7) that makes full DI validation fail even though
/// DbContext itself resolves fine standalone. Standard EF Core pattern for exactly
/// this situation:
/// https://learn.microsoft.com/ef/core/cli/dbcontext-creation#from-a-design-time-factory
///
/// `migrations add` does NOT need a live database connection (this connection string
/// is only used to pick the provider/generate SQL), so the fallback below works for
/// that. `database update` DOES need a real, reachable SQL Server - set the
/// DOMAINCOPILOT_SQL_CONNECTION environment variable to your real connection string
/// before running that command, or edit the fallback directly.
/// </summary>
public sealed class DomainCopilotDbContextFactory : IDesignTimeDbContextFactory<DomainCopilotDbContext>
{
    public DomainCopilotDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("DOMAINCOPILOT_SQL_CONNECTION")
            ?? @"Data Source=DESKTOP-KNGP9KR\SQLEXPRESS;Initial Catalog=DomainCopilot;Integrated Security=True;Encrypt=True;Trust Server Certificate=True";

        var optionsBuilder = new DbContextOptionsBuilder<DomainCopilotDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new DomainCopilotDbContext(optionsBuilder.Options);
    }
}