using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace SIAP.Api.Middleware;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;
        var path = context.Request.Path;
        var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        var userAgent = context.Request.Headers["User-Agent"].ToString();
        var traceId = context.TraceIdentifier;

        var source = "External Client";
        if (userAgent.Contains("HttpClient") || userAgent.Contains("BackgroundService") || userAgent.Contains("PowerShell"))
        {
            source = "Internal / Script";
        }

        _logger.LogInformation(
            "[{TraceId}] Incoming {Method} {Path} from {RemoteIp} ({Source}). UserAgent: {UserAgent}",
            traceId, method, path, remoteIp, source, string.IsNullOrWhiteSpace(userAgent) ? "None" : userAgent);

        await _next(context);
    }
}
