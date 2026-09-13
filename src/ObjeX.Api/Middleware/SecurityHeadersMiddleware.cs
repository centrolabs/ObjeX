namespace ObjeX.Api.Middleware;

public static class SecurityHeadersMiddleware
{
    /// <summary>
    /// Baseline response headers for both ports. CSP is intentionally absent: Blazor Server needs
    /// inline scripts and a SignalR WebSocket, which makes a safe policy non-trivial.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, bool includeHsts)
    {
        return app.Use(async (ctx, next) =>
        {
            ctx.Response.Headers.Remove("X-Powered-By");
            // Blazor's framework files are served with their real content type; nosniff would only add noise there.
            if (!ctx.Request.Path.StartsWithSegments("/_framework"))
                ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
            ctx.Response.Headers["X-Permitted-Cross-Domain-Policies"] = "none";
            ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            if (includeHsts)
                ctx.Response.Headers["Strict-Transport-Security"] = "max-age=63072000; includeSubDomains";
            await next();
        });
    }
}
