using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainCopilot.Infrastructure.Identity;

/// <summary>The two FR-8 roles. Fixed Guids so Role HasData seeding is deterministic across every migration run/environment.</summary>
public static class Roles
{
    public const string Clinician = "Clinician";
    public const string Admin = "Admin";

    public static readonly Guid ClinicianRoleId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid AdminRoleId = Guid.Parse("22222222-2222-2222-2222-222222222222");
}
