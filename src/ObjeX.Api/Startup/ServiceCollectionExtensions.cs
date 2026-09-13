using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ObjeX.Api.Options;
using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Infrastructure.Data;
using ObjeX.Infrastructure.Hashing;
using ObjeX.Infrastructure.Health;
using ObjeX.Infrastructure.Metadata;
using ObjeX.Infrastructure.Storage;
using Radzen;

namespace ObjeX.Api.Startup;

/// <summary>Service registration, one method per concern. Program.cs composes these.</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddObjeXDatabase(this IServiceCollection services, DatabaseOptions database, IHostEnvironment environment)
    {
        services.AddDbContext<ObjeXDbContext>(options =>
        {
            if (database.IsPostgreSql)
                options.UseNpgsql(database.ConnectionString, o =>
                {
                    o.CommandTimeout(30);
                    o.MigrationsAssembly("ObjeX.Migrations.PostgreSql");
                });
            else
                options.UseSqlite(database.ConnectionString, o => o.CommandTimeout(30));

            options.UseSnakeCaseNamingConvention();

            if (environment.IsDevelopment())
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }
        });

        return services;
    }

    public static IServiceCollection AddObjeXStorage(this IServiceCollection services, string blobBasePath)
    {
        services.AddScoped<IMetadataService, SqliteMetadataService>();
        services.AddSingleton<IHashService, Sha256HashService>();

        // Registered under the concrete type first so Hangfire jobs can take it directly (BasePath is
        // internal to Infrastructure), then aliased to the interface for everyone else.
        services.AddSingleton(sp => new FileSystemStorageService(
            blobBasePath,
            sp.GetRequiredService<IHashService>(),
            sp.GetRequiredService<ILogger<FileSystemStorageService>>()));
        services.AddSingleton<IObjectStorageService>(sp => sp.GetRequiredService<FileSystemStorageService>());

        services.AddHealthChecks()
            .AddDbContextCheck<ObjeXDbContext>(tags: ["ready"])
            .AddCheck<BlobStorageHealthCheck>("blob_storage", tags: ["ready"]);

        return services;
    }

    public static IServiceCollection AddObjeXIdentity(this IServiceCollection services, AuthOptions auth)
    {
        services.AddIdentity<User, IdentityRole>(options =>
            {
                // Relaxed password rules for MVP
                options.Password.RequireDigit = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 4;
                options.User.RequireUniqueEmail = true;

                // Per-account lockout on failed logins (see AuthOptions). Enforced via lockoutOnFailure in AccountEndpoints.
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = auth.Lockout.MaxFailedAttempts;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(auth.Lockout.DurationMinutes);
            })
            .AddEntityFrameworkStores<ObjeXDbContext>()
            .AddDefaultTokenProviders()
            .AddClaimsPrincipalFactory<UserClaimsPrincipalFactory<User, IdentityRole>>();

        services.Configure<SecurityStampValidatorOptions>(options =>
            options.ValidationInterval = TimeSpan.FromMinutes(5));

        services.AddAuthentication(options =>
        {
            options.DefaultScheme = IdentityConstants.ApplicationScheme;
            options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
        });
        services.AddAuthorization();

        services.ConfigureApplicationCookie(options =>
        {
            options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
            options.SlidingExpiration = true;

            // API callers expect status codes, not a redirect to the login page.
            options.Events.OnRedirectToLogin = ctx => StatusOrRedirect(ctx, StatusCodes.Status401Unauthorized);
            options.Events.OnRedirectToAccessDenied = ctx => StatusOrRedirect(ctx, StatusCodes.Status403Forbidden);
        });

        return services;

        static Task StatusOrRedirect(Microsoft.AspNetCore.Authentication.RedirectContext<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions> ctx, int statusCode)
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
                ctx.Response.StatusCode = statusCode;
            else
                ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        }
    }

    public static IServiceCollection AddObjeXBlazor(this IServiceCollection services)
    {
        services.AddRazorComponents()
            .AddInteractiveServerComponents()
            // Uploads travel over the SignalR circuit (InputFile), so the hub must accept large messages.
            .AddHubOptions(o => o.MaximumReceiveMessageSize = 500 * 1024 * 1024);
        services.AddHttpContextAccessor();
        services.AddRadzenComponents();
        services.AddScoped<ThemeService>();
        services.AddCascadingAuthenticationState();
        services.AddResponseCompression(options => options.EnableForHttps = true);

        return services;
    }
}
