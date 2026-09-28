using SIPENTA.Api.DTOs.Dashboard;

namespace SIPENTA.Api.Services.Interfaces;

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(Guid? userId = null, int? userBidangId = null, bool isAdmin = false);
}
