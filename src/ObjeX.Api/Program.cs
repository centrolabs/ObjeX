using Hangfire;
using ObjeX.Api.Auth;
using ObjeX.Api.Components;
using ObjeX.Api.Endpoints;
using ObjeX.Api.Metrics;
using ObjeX.Api.Middleware;
using ObjeX.Api.Options;
using ObjeX.Api.S3;
using ObjeX.Api.Startup;
using ObjeX.Infrastructure.Options;
using Prometheus;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration -------------------------------------------------------------------------
// Relative paths in config (DB file, blob root, log files) resolve against the content root:
// the project directory under `dotnet run`, /app in the container. Never against the process
// working directory, which differs between IDE, CLI and service managers.
string ResolvePath(string path) => Path.GetFullPath(path, builder.Environment.ContentRootPath);

foreach (var sink in builder.Configuration.GetSection("Serilog:WriteTo").GetChildren())
{
    var key = $"{sink.Path}:Args:path";
    if (builder.Configuration[key] is { Length: > 0 } logPath && !Path.IsPathRooted(logPath))
        builder.Configuration[key] = ResolvePath(logPath);
}

T Options<T>(string section) where T : new() => builder.Configuration.GetSection(section).Get<T>() ?? new T();

var server = Options<ServerOptions>(ServerOptions.SectionName);
server.Validate();
var auth = Options<AuthOptions>(AuthOptions.SectionName);
auth.Validate();
var reverseProxy = Options<ReverseProxyOptions>(ReverseProxyOptions.SectionName);
var storage = Options<StorageOptions>(StorageOptions.SectionName);
var defaultAdmin = Options<DefaultAdminOptions>(DefaultAdminOptions.SectionName);
var seed = Options<SeedOptions>(SeedOptions.SectionName);
var database = DatabaseOptions.Load(builder.Configuration, ResolvePath);
var metrics = Options<MetricsOptions>(MetricsOptions.SectionName);

// ---- Host ----------------------------------------------------------------------------------
builder.Host.UseSerilog((context, config) => config.ReadFrom.Configuration(context.Configuration));

// Ports come from Server:UiPort / Server:S3Port only. Kestrel listeners defined in code take
// precedence over ASPNETCORE_URLS, so that variable is intentionally not used anywhere.
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = storage.MaxUploadBytes;
    o.AddServerHeader = false;
    o.ListenAnyIP(server.UiPort);
    o.ListenAnyIP(server.S3Port);
});

if (reverseProxy.Enabled)
    builder.Services.Configure<ForwardedHeadersOptions>(reverseProxy.Apply);

// ---- Services ------------------------------------------------------------------------------
builder.Services
    .AddObjeXOptions(builder.Configuration)
    .AddObjeXDatabase(database, builder.Environment)
    .AddObjeXStorage(ResolvePath(storage.BasePath))
    .AddObjeXIdentity(auth)
    .AddObjeXBlazor()
    .AddObjeXBackgroundJobs(database)
    .AddS3Api();

if (metrics.Enabled)
    builder.Services.AddHostedService<BucketMetricsSyncJob>();

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app, database, defaultAdmin, seed, metrics.Enabled);

// ---- Pipeline shared by both ports ---------------------------------------------------------
if (reverseProxy.Enabled)
    app.UseForwardedHeaders();
app.UseSerilogRequestLogging();
if (metrics.Enabled)
    app.UseHttpMetrics();
app.UseSecurityHeaders(includeHsts: !app.Environment.IsDevelopment());

// ---- S3 port: terminal branch, see S3Pipeline.cs -------------------------------------------
app.UseS3Api(server.S3Port);

// ---- UI port: Blazor + cookie-authenticated internal endpoints -----------------------------
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = "An error occurred" });
    }));
}
app.UseWhen(
    ctx => !ctx.Request.Path.StartsWithSegments("/api")
        && !ctx.Request.Path.StartsWithSegments("/metrics")
        && !ctx.Request.Path.StartsWithSegments("/_framework")
        && !ctx.Request.Path.StartsWithSegments("/_content"),
    branch => branch.UseStatusCodePagesWithRedirects("/not-found"));
app.UseResponseCompression();
app.UseStaticFiles();
// Explicit so routing runs after the S3 split. Without this call WebApplication inserts
// routing at the very start of the pipeline, ahead of the port check.
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireAuthorizationFilter()]
});
BackgroundJobs.RegisterRecurringJobs(app.Services);

if (metrics.Enabled)
    app.MapObjeXMetrics(metrics);
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(ObjeX.Web.Components.Routes).Assembly);

app.MapDownloadEndpoints();
app.MapPresignEndpoints();
app.MapAccountEndpoints();

app.Run();
