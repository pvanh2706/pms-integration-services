using Microsoft.Extensions.Options;
using PmsIntegration.Host.Options;

namespace PmsIntegration.Host.Middleware;

/// <summary>
/// Validates the fixed PMS token for routes under /api/pms/*.
/// Returns 401 with {"error":"unauthorized"} if token is missing or invalid.
/// </summary>
public sealed class PmsTokenMiddleware
{
    private readonly RequestDelegate _next;

    public PmsTokenMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IOptions<PmsSecurityOptions> securityOptions)
    {
        if (!context.Request.Path.StartsWithSegments("/api/pms"))
        {
            await _next(context);
            return;
        }

        var options = securityOptions.Value;
        var headerName = string.IsNullOrWhiteSpace(options.HeaderName)
            ? "X-PMS-TOKEN"
            : options.HeaderName;

        if (!context.Request.Headers.TryGetValue(headerName, out var token)
            || token != options.FixedToken)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":\"unauthorized\"}");
            return;
        }

        await _next(context);
    }
}
