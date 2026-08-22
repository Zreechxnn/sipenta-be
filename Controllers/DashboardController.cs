using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIAP.Api.Common;
using SIAP.Api.DTOs.Dashboard;
using SIAP.Api.Services.Interfaces;

namespace SIAP.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "admin")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;

    public DashboardController(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        try
        {
            var summary = await _dashboardService.GetSummaryAsync();
            return Ok(ApiResponse<DashboardSummaryDto>.Ok(summary));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<DashboardSummaryDto>.Gagal(ex.Message));
        }
    }
}
