using System.Linq.Expressions;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.Storage;
using Hangfire.Storage.SQLite;
using ObjeX.Api.Options;
using ObjeX.Infrastructure.Jobs;

namespace ObjeX.Api.Startup;

/// <summary>
/// Hangfire wiring. Storage follows the database provider and reuses the same SQLite file or
/// PostgreSQL database. The recurring schedule is declared in one place; whatever else Hangfire still
/// holds in storage from earlier versions is removed, because a recurring job whose class no longer
/// exists fails to load on every tick and is retried forever.
/// </summary>
public static class BackgroundJobs
{
    public static IServiceCollection AddObjeXBackgroundJobs(this IServiceCollection services, DatabaseOptions database)
    {
        services.AddHangfire(config =>
        {
            config.UseSimpleAssemblyNameTypeSerializer()
                  .UseRecommendedSerializerSettings();

            if (database.IsPostgreSql)
                config.UsePostgreSqlStorage(o => o.UseNpgsqlConnection(database.ConnectionString));
            else
                config.UseSQLiteStorage(database.SqliteFilePath!); // file path, not an EF connection string
        });
        services.AddHangfireServer();

        services.AddScoped<CleanupOrphanedBlobsJob>();
        services.AddScoped<VerifyBlobIntegrityJob>();
        services.AddScoped<CleanupAbandonedMultipartJob>();

        return services;
    }

    /// <summary>Declares the recurring schedule and prunes recurring jobs this version does not declare.</summary>
    public static void RegisterRecurringJobs(IServiceProvider services)
    {
        var manager = services.GetRequiredService<IRecurringJobManager>();
        var storage = services.GetRequiredService<JobStorage>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(BackgroundJobs));
        var declared = new HashSet<string>(StringComparer.Ordinal);

        Declare<CleanupOrphanedBlobsJob>("cleanup-orphaned-blobs", job => job.ExecuteAsync(), Cron.Weekly(DayOfWeek.Sunday, 3));
        Declare<VerifyBlobIntegrityJob>("verify-blob-integrity", job => job.ExecuteAsync(), Cron.Weekly(DayOfWeek.Sunday, 4));
        Declare<CleanupAbandonedMultipartJob>("cleanup-abandoned-multipart", job => job.ExecuteAsync(), Cron.Weekly(DayOfWeek.Sunday, 5));

        using var connection = storage.GetConnection();
        foreach (var job in connection.GetRecurringJobs())
        {
            if (declared.Contains(job.Id))
                continue;

            manager.RemoveIfExists(job.Id);
            logger.LogWarning(
                "Removed recurring job '{JobId}' left behind by an earlier version{Detail}.",
                job.Id, job.LoadException is null ? "" : " (its job type no longer exists)");
        }

        void Declare<TJob>(string id, Expression<Func<TJob, Task>> call, string cron)
        {
            manager.AddOrUpdate(id, call, cron);
            declared.Add(id);
        }
    }
}
