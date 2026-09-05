using DomainCopilot.Application.CostGovernor.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DomainCopilot.Api.Controllers;

/// <summary>Prompt 11.2's admin-only aggregated spend view.</summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public sealed class AdminController : ControllerBase
{
    private readonly ISpendReportService _spendReportService;

    public AdminController(ISpendReportService spendReportService)
    {
        _spendReportService = spendReportService ?? throw new ArgumentNullException(nameof(spendReportService));
    }

    [HttpGet("spend")]
    public async Task<IActionResult> GetSpend(CancellationToken ct)
    {
        var report = await _spendReportService.GetAggregatedSpendAsync(ct);
        return Ok(report);
    }
}