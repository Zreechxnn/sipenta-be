using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SIAP.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly HealthCheckService _healthCheckService;

    public HealthController(HealthCheckService healthCheckService)
    {
        _healthCheckService = healthCheckService;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var report = await _healthCheckService.CheckHealthAsync();
        var status = report.Status == HealthStatus.Healthy ? "Healthy" : "Unhealthy";
        return Ok(new { status, results = report.Entries.Select(e => new { key = e.Key, status = e.Value.Status.ToString() }) });
    }
}