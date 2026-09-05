using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;

namespace DomainCopilot.Infrastructure.Identity;

/// <summary>
/// Fixed demo accounts seeded via migration HasData (Prompt 11.1's explicit
/// requirement), so `docker compose up` + migrate gives a fully working system with
/// no manual DBA step (Section 7). Password hashes are precomputed here (Identity's
/// PasswordHasher has no real DI dependencies, so it can run standalone at
/// migration-authoring time) rather than seeded at runtime - HasData seeding must be
/// static, deterministic data, not something computed fresh at app startup.
///
/// CREDENTIALS (local dev/demo ONLY - never real accounts): both use password
/// "Demo#12345" - documented here AND must be documented in README's seeded demo
/// accounts section (Section 5 requirement) so anyone running the 5-Minute Demo
/// Path knows them.
/// </summary>
public static class DemoUsers
{
    public static readonly Guid ClinicianUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid AdminUserId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private const string DemoPassword = "Demo#12345";

    public static ApplicationUser BuildClinician()
    {
        var user = new ApplicationUser
        {
            Id = ClinicianUserId,
            UserName = "clinician@demo.local",
            NormalizedUserName = "CLINICIAN@DEMO.LOCAL",
            Email = "clinician@demo.local",
            NormalizedEmail = "CLINICIAN@DEMO.LOCAL",
            EmailConfirmed = true,
            DisplayName = "Dr. Demo Clinician",
            SecurityStamp = "33333333-0000-0000-0000-000000000001",
            ConcurrencyStamp = "33333333-0000-0000-0000-000000000002"
        };
        user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, DemoPassword);
        return user;
    }

    public static ApplicationUser BuildAdmin()
    {
        var user = new ApplicationUser
        {
            Id = AdminUserId,
            UserName = "admin@demo.local",
            NormalizedUserName = "ADMIN@DEMO.LOCAL",
            Email = "admin@demo.local",
            NormalizedEmail = "ADMIN@DEMO.LOCAL",
            EmailConfirmed = true,
            DisplayName = "Demo Admin",
            SecurityStamp = "44444444-0000-0000-0000-000000000001",
            ConcurrencyStamp = "44444444-0000-0000-0000-000000000002"
        };
        user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, DemoPassword);
        return user;
    }
}