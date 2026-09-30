using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ObjeX.Api;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Tests;

public class ObjeXFactory : WebApplicationFactory<ApiAssemblyMarker>
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"objex-test-{Guid.NewGuid():N}");

    /// <summary>A PostgreSQL connection string without Database=; when set, the suite runs on PostgreSQL, one database per factory.</summary>
    public const string PostgresVariable = "OBJEX_TEST_POSTGRES";

    private readonly string? _postgres = Environment.GetEnvironmentVariable(PostgresVariable) is { Length: > 0 } server
        ? $"{server};Database=objex_test_{Guid.NewGuid():N}"
        : null;

    /// <summary>Test-only header that stands in for the TCP port a request arrived on.</summary>
    public const string PortHeader = "X-ObjeX-Test-Port";

    /// <summary>Test-only header carrying the path as sent; TestServer leaves RawTarget empty, Kestrel does not.</summary>
    public const string RawTargetHeader = "X-ObjeX-Test-Raw-Target";
    public const int UiPort = 9001;
    public const int S3Port = 9000;

    public string AccessKeyId => "OBXTEST12345678901";
    public string SecretAccessKey => "TestSecretKeyForIntegrationTests1234567890ab";

    public string BlobBasePath => Path.Combine(_tempDir, "blobs");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var dbPath = Path.Combine(_tempDir, "test.db");
        Directory.CreateDirectory(_tempDir);

        builder.UseEnvironment("Production"); // avoid dev exception page noise
        // Production skips the static web assets manifest; without it, framework and class-library
        // assets (_framework/*, _content/*) are invisible to TestServer.
        builder.UseStaticWebAssets();

        builder.UseSetting("ConnectionStrings:DefaultConnection", _postgres ?? $"Data Source={dbPath}");
        builder.UseSetting("Storage:BasePath", BlobBasePath);
        builder.UseSetting("Database:Provider", _postgres is null ? "sqlite" : "postgresql");
        builder.UseSetting("Server:UiPort", UiPort.ToString());
        builder.UseSetting("Server:S3Port", S3Port.ToString());
        builder.UseSetting("Metrics:Enabled", "false");
        builder.UseSetting("Auth:Lockout:MaxFailedAttempts", "5");
        builder.UseSetting("Auth:Lockout:DurationMinutes", "5");
        builder.UseSetting("Seed:S3Credential:AccessKeyId", AccessKeyId);
        builder.UseSetting("Seed:S3Credential:SecretAccessKey", SecretAccessKey);
        builder.UseSetting("Seed:S3Credential:Name", "test-credential");
        builder.UseSetting("Seed:Buckets", "test-bucket");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Fatal");

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IStartupFilter, TestPortStartupFilter>();

            // Remove Hangfire background server — it holds SQLite connections that
            // throw "out of memory" on disposal when the temp DB is cleaned up.
            // Tests don't need background job processing.
            services.RemoveAll<IHostedService>();

            // Suppress PendingModelChangesWarning — StorageUsedBytes lives on as a shadow
            // property in ObjeXDbContext, which EF flags as a pending change.
            // EF registers the options with TryAdd, so the originals are removed first or this override is a silent no-op.
            services.RemoveAll<DbContextOptions<ObjeXDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.AddDbContextFactory<ObjeXDbContext>(options =>
            {
                if (_postgres is null)
                    options.UseSqlite($"Data Source={dbPath}", o => o.CommandTimeout(30));
                else
                    options.UseNpgsql(_postgres, o =>
                    {
                        o.CommandTimeout(30);
                        o.MigrationsAssembly("ObjeX.Migrations.PostgreSql");
                    });
                options.UseSnakeCaseNamingConvention();
                options.ConfigureWarnings(w =>
                    w.Ignore(RelationalEventId.PendingModelChangesWarning));
            });
        });
    }

    /// <summary>
    /// Client whose requests enter the S3 pipeline. S3 clients never follow redirects, so a
    /// redirect coming out of the S3 port is always a bug worth asserting on.
    /// </summary>
    public HttpClient CreateS3Client()
    {
        var client = CreateDefaultClient(new RawTargetHandler());
        client.DefaultRequestHeaders.Host = "localhost:9000";
        client.DefaultRequestHeaders.Add(PortHeader, S3Port.ToString());
        return client;
    }

    public IServiceScope CreateScope() => Services.CreateScope();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }
}

/// <summary>
/// TestServer has no sockets, so Connection.LocalPort is always 0 and every request would land
/// in the UI pipeline. Requests carrying <see cref="ObjeXFactory.PortHeader"/> get that value as
/// LocalPort, which is what the S3/UI split keys on. The Host header stays free, so proxy
/// scenarios (Host without a port) are testable.
/// </summary>
file class TestPortStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (ctx, nextMiddleware) =>
            {
                if (int.TryParse(ctx.Request.Headers[ObjeXFactory.PortHeader], out var port))
                    ctx.Connection.LocalPort = port;
                if (ctx.Request.Headers[ObjeXFactory.RawTargetHeader] is [{ } rawTarget])
                    ctx.Features.Get<IHttpRequestFeature>()!.RawTarget = rawTarget;
                await nextMiddleware();
            });
            next(app);
        };
    }
}

file class RawTargetHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.TryAddWithoutValidation(ObjeXFactory.RawTargetHeader,
            request.RequestUri!.GetComponents(UriComponents.PathAndQuery, UriFormat.UriEscaped));
        return base.SendAsync(request, cancellationToken);
    }
}
