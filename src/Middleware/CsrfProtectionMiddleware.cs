using System.Security.Cryptography;

namespace SIAP.Api.Middleware;

public class CsrfProtectionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CsrfProtectionMiddleware> _logger;

    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "HEAD", "OPTIONS", "TRACE"
    };

    private readonly HashSet<string> _allowedOrigins;

    public CsrfProtectionMiddleware(RequestDelegate next, ILogger<CsrfProtectionMiddleware> logger, IConfiguration configuration)
    {
        _next = next;
        _logger = logger;

        _allowedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allowedOriginsConfig = configuration["Cors:AllowedOrigins"];
        if (!string.IsNullOrWhiteSpace(allowedOriginsConfig))
        {
            foreach (var origin in allowedOriginsConfig.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = origin.Trim().TrimEnd('/');
                if (!string.IsNullOrEmpty(trimmed))
                {
                    _allowedOrigins.Add(trimmed);
                }
            }
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Skip non-API and documentation paths
        if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/uploads", StringComparison.OrdinalIgnoreCase) ||
            path == "/" ||
            SafeMethods.Contains(context.Request.Method))
        {
            await _next(context);
            return;
        }

        // 0. Block automated CLI tools (curl, Postman, python-requests, wget)
        var userAgent = context.Request.Headers.UserAgent.ToString();
        if (userAgent.StartsWith("curl/", StringComparison.OrdinalIgnoreCase) ||
            userAgent.StartsWith("PostmanRuntime/", StringComparison.OrdinalIgnoreCase) ||
            userAgent.StartsWith("python-requests/", StringComparison.OrdinalIgnoreCase) ||
            userAgent.StartsWith("Wget/", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Access blocked: Automated CLI tool User-Agent '{UserAgent}' on {Path}", userAgent, path);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"message\": \"Permintaan ditolak: Akses langsung via CLI/alat otomatis tidak diizinkan.\"}");
            return;
        }

        // 1. Validate Origin header if present
        if (context.Request.Headers.TryGetValue("Origin", out var originValues) && originValues.Count > 0)
        {
            var origin = originValues.ToString().TrimEnd('/');
            if (!IsOriginAllowed(origin, context.Request.Host.ToString()))
            {
                _logger.LogWarning("CSRF validation failed: Untrusted Origin '{Origin}' on {Method} {Path}", origin, context.Request.Method, path);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"message\": \"Permintaan ditolak: Origin tidak diizinkan (Proteksi CSRF).\"}");
                return;
            }
        }
        else if (context.Request.Headers.TryGetValue("Referer", out var refererValues) && refererValues.Count > 0)
        {
            var referer = refererValues.ToString();
            if (Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
            {
                var refererOrigin = $"{refererUri.Scheme}://{refererUri.Authority}";
                if (!IsOriginAllowed(refererOrigin, context.Request.Host.ToString()))
                {
                    _logger.LogWarning("CSRF validation failed: Untrusted Referer '{Referer}' on {Method} {Path}", referer, context.Request.Method, path);
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{\"message\": \"Permintaan ditolak: Referer tidak diizinkan (Proteksi CSRF).\"}");
                    return;
                }
            }
        }

        // 2. Custom Anti-CSRF Header / Double Submit Cookie Verification
        // If cookie authentication is used, require custom header or valid CSRF token
        var hasAuthCookie = context.Request.Cookies.ContainsKey("sipenta_token");
        var hasAuthorizationHeader = context.Request.Headers.ContainsKey("Authorization");

        if (hasAuthCookie && !hasAuthorizationHeader)
        {
            var hasCustomHeader =
                (context.Request.Headers.TryGetValue("X-Requested-With", out var xrw) && xrw == "XMLHttpRequest") ||
                (context.Request.Headers.TryGetValue("X-CSRF-Protection", out var xcp) && xcp == "1");

            var hasDoubleSubmitMatch = false;
            if (context.Request.Cookies.TryGetValue("sipenta_csrf", out var cookieCsrf) && !string.IsNullOrEmpty(cookieCsrf))
            {
                if (context.Request.Headers.TryGetValue("X-CSRF-Token", out var headerCsrf) && headerCsrf == cookieCsrf)
                {
                    hasDoubleSubmitMatch = true;
                }
            }

            // Also allow SignalR negotiation
            var isSignalR = path.StartsWith("/hubs", StringComparison.OrdinalIgnoreCase);

            if (!hasCustomHeader && !hasDoubleSubmitMatch && !isSignalR)
            {
                _logger.LogWarning("CSRF validation failed: Missing anti-CSRF custom header or token on {Method} {Path}", context.Request.Method, path);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"message\": \"Permintaan ditolak: Header atau token anti-CSRF tidak valid.\"}");
                return;
            }
        }

        await _next(context);
    }

    private bool IsOriginAllowed(string origin, string currentHost)
    {
        if (string.IsNullOrWhiteSpace(origin)) return false;

        var cleanOrigin = origin.TrimEnd('/');
        if (_allowedOrigins.Contains(cleanOrigin)) return true;

        if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            if (string.Equals(uri.Authority, currentHost, StringComparison.OrdinalIgnoreCase))
                return true;

            if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
