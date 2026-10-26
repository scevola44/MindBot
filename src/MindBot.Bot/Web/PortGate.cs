using MindBot.Core.Options;

namespace MindBot.Bot.Web;

/// <summary>
/// Keeps the two listeners apart: /health answers only on the in-container health port, and the
/// chat API and web client only on the web port.
/// <para>
/// Decided by the port the connection actually arrived on, not by the Host header
/// (RequireHost), because the client controls the Host header and could reach /health through
/// the VPN-exposed port just by setting it.
/// </para>
/// </summary>
public sealed class PortGateMiddleware(RequestDelegate next, int? webPort)
{
    public const string HealthPath = "/health";

    public Task InvokeAsync(HttpContext context)
    {
        return IsAllowed(context.Connection.LocalPort, context.Request.Path, webPort)
            ? next(context)
            : NotFound(context);
    }

    public static bool IsAllowed(int localPort, PathString path, int? webPort)
    {
        var isHealth = path.Equals(HealthPath, StringComparison.OrdinalIgnoreCase);

        if (localPort == WebOptions.HealthPort)
        {
            return isHealth;
        }

        return webPort is not null && localPort == webPort && !isHealth;
    }

    private static Task NotFound(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Hardening for everything the web port serves. The web client keeps its device token in
/// browser storage, so a script injected into the page could read it; the CSP allows scripts and
/// connections to this origin only, which closes the usual ways one would get there.
/// 'unsafe-inline' for styles is required by react-native-web, which writes inline styles; it
/// does not allow inline script.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; connect-src 'self'; img-src 'self' data:; " +
        "style-src 'self' 'unsafe-inline'; font-src 'self' data:; object-src 'none'; " +
        "base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.ContentSecurityPolicy = ContentSecurityPolicy;
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers.XFrameOptions = "DENY";

        // Chat content must never sit in a shared or disk cache.
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            headers.CacheControl = "no-store";
        }

        return next(context);
    }
}
