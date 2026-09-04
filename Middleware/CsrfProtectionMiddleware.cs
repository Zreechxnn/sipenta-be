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

    private static readonly HashSet<string> AllowedOrigins = new(StringComparer.OrdinalIgnoreCase)
    {
        "https://siap-fe.rechanpage.my.id",
        "https://sipenta-fe.vercel.app",
        "http://localhost:3000",
        "http://localhost:3001",
        "http://127.0.0.1:3000",
        "http://127.0.0.1:3001"
    };

    public CsrfProtectionMiddleware(RequestDelegate next, ILogger<CsrfProtectionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
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

    private static bool IsOriginAllowed(string origin, string currentHost)
    {
        if (string.IsNullOrWhiteSpace(origin)) return false;

        if (AllowedOrigins.Contains(origin)) return true;

        if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            if (string.Equals(uri.Authority, currentHost, StringComparison.OrdinalIgnoreCase))
                return true;

            if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                origin.EndsWith(".vercel.app", StringComparison.OrdinalIgnoreCase) ||
                origin.EndsWith(".rechanpage.my.id", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
