using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;

namespace DomainCopilot.Infrastructure.Identity;

/// <summary>
/// FR-8's Identity user. Deliberately lives in Infrastructure, never Domain or
/// Application (project brief Section 3: Domain has zero references to ASP.NET
/// Core; Application depends only on Domain) - IdentityUser is a framework type.
/// Guid keys chosen to match every other id in this codebase (AgentRun.Id,
/// ClinicalCase.Id, etc.) rather than Identity's string-key default.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
}