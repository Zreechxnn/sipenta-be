using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIAP.Api.Common;
using SIAP.Api.DTOs.Dashboard;
using SIAP.Api.Repositories.Interfaces;
using SIAP.Api.Services.Interfaces;

namespace SIAP.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "super-admin,admin,kasubag")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;
    private readonly IUserRepository _userRepository;

    public DashboardController(IDashboardService dashboardService, IUserRepository userRepository)
    {
        _dashboardService = dashboardService;
        _userRepository = userRepository;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        try
        {
            var isAdmin = User.IsInRole("super-admin") || User.IsInRole("admin");
            Guid? userId = null;
            int? userBidangId = null;

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(userIdStr, out var parsedUserId))
            {
                userId = parsedUserId;
                var dbUser = await _userRepository.GetByIdAsync(parsedUserId);
                userBidangId = dbUser?.BidangId;
            }

            var summary = await _dashboardService.GetSummaryAsync(userId, userBidangId, isAdmin);
            return Ok(ApiResponse<DashboardSummaryDto>.Ok(summary));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<DashboardSummaryDto>.Gagal(ex.Message));
        }
    }
}
