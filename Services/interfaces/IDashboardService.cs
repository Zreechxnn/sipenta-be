using SIAP.Api.DTOs.Dashboard;

namespace SIAP.Api.Services.Interfaces;

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(Guid? userId = null, int? userBidangId = null, bool isAdmin = false);
}
