using ObjeX.Api.Endpoints.S3Endpoints;
using ObjeX.Api.Middleware;

namespace ObjeX.Api.S3;

/// <summary>
/// The S3 API as a self-contained request pipeline, mounted in front of the UI pipeline.
/// Every request that arrives on the S3 port is handled here and never reaches the UI middleware
/// (status-code redirects, cookie auth, Blazor). Routing inside is independent of the UI routing,
/// so S3 routes are invisible on the UI port and UI routes are invisible on the S3 port.
/// </summary>
public static class S3Pipeline
{
    /// <summary>S3 clients live on other origins (browser SDKs, presigned POST forms), so the S3 port is fully open to CORS.</summary>
    public static IServiceCollection AddS3Api(this IServiceCollection services)
    {
        services.AddCors(options => options.AddPolicy("S3", policy =>
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));
        return services;
    }

    public static WebApplication UseS3Api(this WebApplication app, int s3Port)
    {
        // A fresh ApplicationBuilder has its own endpoint route builder. Branching off `app`
        // would share the global one and let UI endpoints match inside this pipeline.
        var s3 = new ApplicationBuilder(app.Services, ((IApplicationBuilder)app).ServerFeatures);

        s3.UseExceptionHandler(errors => errors.Run(ctx =>
            S3Xml.WriteErrorAsync(ctx, S3Errors.InternalError,
                "We encountered an internal error. Please try again.", StatusCodes.Status500InternalServerError)));

        s3.UseCors("S3");
        s3.UseMiddleware<SigV4AuthMiddleware>();
        s3.UseRouting();
        s3.UseAuthorization();
        s3.UseEndpoints(endpoints =>
        {
            var group = endpoints.MapGroup("/").RequireAuthorization();
            group.MapS3BucketEndpoints();
            group.MapS3ObjectEndpoints();
            group.MapS3MultipartEndpoints();
            group.MapS3PostObjectEndpoints();
        });

        // Nothing on the S3 port ever answers with HTML or an empty body.
        s3.Run(ctx => S3Xml.WriteErrorAsync(ctx, S3Errors.NoSuchKey,
            "The specified resource does not exist.", StatusCodes.Status404NotFound));

        var pipeline = s3.Build();

        app.Use(async (ctx, next) =>
        {
            if (ctx.Connection.LocalPort == s3Port)
                await pipeline(ctx);
            else
                await next(ctx);
        });

        return app;
    }
}
