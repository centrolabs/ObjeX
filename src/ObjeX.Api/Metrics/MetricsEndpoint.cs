using System.Security.Cryptography;
using System.Text;
using ObjeX.Api.Options;
using Prometheus;

namespace ObjeX.Api.Metrics;

public static class MetricsEndpoint
{
    /// <summary>/metrics is open on the UI port unless Metrics:Token is set; then the scraper must send it as a Bearer token.</summary>
    public static void MapObjeXMetrics(this WebApplication app, MetricsOptions options)
    {
        if (!string.IsNullOrEmpty(options.Token))
        {
            var expected = Encoding.UTF8.GetBytes("Bearer " + options.Token);
            // StartsWithSegments, because routing also serves the endpoint for "/metrics/" and any casing.
            app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/metrics"), branch => branch.Use(async (ctx, next) =>
            {
                var provided = Encoding.UTF8.GetBytes(ctx.Request.Headers.Authorization.ToString());
                if (!CryptographicOperations.FixedTimeEquals(expected, provided))
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    ctx.Response.Headers.WWWAuthenticate = "Bearer";
                    return;
                }
                await next();
            }));
        }

        app.MapMetrics("/metrics");
    }
}
