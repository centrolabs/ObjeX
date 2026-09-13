using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ObjeX.Api.Metrics;
using ObjeX.Api.Options;
using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Core.Validation;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Api.Startup;

/// <summary>
/// Everything that has to be true about the database before the first request: schema, SQLite
/// pragmas, roles, the default admin, seeded buckets and credentials. Runs once, before the pipeline
/// is built. Every step is idempotent.
/// </summary>
public static class DatabaseInitializer
{
    private static readonly string[] Roles = ["Admin", "Manager", "User"];

    public static async Task InitializeAsync(
        WebApplication app,
        DatabaseOptions database,
        DefaultAdminOptions defaultAdmin,
        SeedOptions seed,
        bool metricsEnabled)
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = app.Logger;
        var db = services.GetRequiredService<ObjeXDbContext>();

        if (database.SqliteFilePath is not null)
            Directory.CreateDirectory(Path.GetDirectoryName(database.SqliteFilePath)!);

        logger.LogInformation("Database provider: {Provider}", database.Provider);

        if (database.AutoMigrate)
        {
            logger.LogWarning("Running database migrations on startup. Ensure a backup exists before migrating in production. Set Database:AutoMigrate=false to disable.");
            db.Database.Migrate();
        }
        else
        {
            logger.LogInformation("Database:AutoMigrate is disabled — skipping automatic migrations.");
        }

        if (database.IsSqlite)
        {
            // WAL allows concurrent reads during writes; synchronous=NORMAL reduces fsync overhead safely;
            // busy_timeout retries for 5s on SQLITE_BUSY before throwing. All three persist in the DB file.
            db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
            db.Database.ExecuteSqlRaw("PRAGMA synchronous=NORMAL;");
            db.Database.ExecuteSqlRaw("PRAGMA busy_timeout=5000;");
        }

        await EnsureRolesAsync(services);
        var admin = await EnsureDefaultAdminAsync(services, defaultAdmin, logger);
        await SeedAsync(services, db, seed, admin, logger);

        if (metricsEnabled)
        {
            foreach (var bucket in await db.Buckets.ToListAsync())
                ObjeXMetrics.SetBucketStats(bucket.Name, bucket.TotalSize, bucket.ObjectCount);
        }
    }

    private static async Task EnsureRolesAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    /// <summary>Creates the configured admin if no user with that name exists. Never touches an existing one.</summary>
    private static async Task<User?> EnsureDefaultAdminAsync(IServiceProvider services, DefaultAdminOptions options, ILogger logger)
    {
        var userManager = services.GetRequiredService<UserManager<User>>();

        var existing = await userManager.FindByNameAsync(options.Username);
        if (existing is not null)
            return existing;

        var admin = new User { UserName = options.Username, Email = options.Email, EmailConfirmed = true };
        var result = await userManager.CreateAsync(admin, options.Password);
        if (!result.Succeeded)
        {
            logger.LogError("Could not create default admin '{Username}': {Errors}",
                options.Username, string.Join("; ", result.Errors.Select(e => e.Description)));
            return null;
        }

        await userManager.AddToRoleAsync(admin, "Admin");
        logger.LogWarning(
            "Default admin user '{Username}' created. Change the credentials before exposing this instance publicly " +
            "(appsettings.json: DefaultAdmin:Password — Docker: DefaultAdmin__Password).", options.Username);
        return admin;
    }

    private static async Task SeedAsync(IServiceProvider services, ObjeXDbContext db, SeedOptions seed, User? admin, ILogger logger)
    {
        var bucketNames = seed.BucketNames.ToList();
        if (!seed.S3Credential.IsSet && bucketNames.Count == 0)
            return;

        if (admin is null)
        {
            logger.LogWarning("Cannot seed S3 credential or buckets: admin user not found.");
            return;
        }

        if (seed.S3Credential.IsSet && !await db.S3Credentials.AnyAsync(c => c.AccessKeyId == seed.S3Credential.AccessKeyId))
        {
            var name = seed.S3Credential.Name ?? "seed-credential";
            db.S3Credentials.Add(new S3Credential
            {
                Name = name,
                AccessKeyId = seed.S3Credential.AccessKeyId!,
                SecretAccessKey = seed.S3Credential.SecretAccessKey!,
                UserId = admin.Id,
            });
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded S3 credential '{Name}' (AccessKeyId: {AccessKeyId})", name, seed.S3Credential.AccessKeyId);
        }

        if (bucketNames.Count == 0)
            return;

        var metadata = services.GetRequiredService<IMetadataService>();
        foreach (var bucketName in bucketNames)
        {
            if (await db.Buckets.AnyAsync(b => b.Name == bucketName))
                continue;

            if (BucketNameValidator.GetValidationError(bucketName) is { } error)
            {
                logger.LogWarning("Skipping invalid seed bucket '{Name}': {Error}", bucketName, error);
                continue;
            }

            await metadata.CreateBucketAsync(new Bucket { Name = bucketName, OwnerId = admin.Id });
            logger.LogInformation("Seeded bucket '{Name}'", bucketName);
        }
    }
}
