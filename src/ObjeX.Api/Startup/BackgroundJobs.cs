using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;
using Hangfire.Storage;
using Hangfire.Storage.SQLite;
using ObjeX.Api.Jobs;
using ObjeX.Api.Options;
using Microsoft.EntityFrameworkCore;
using ObjeX.Core.Interfaces;
using ObjeX.Infrastructure.Data;
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
    /// <summary>How long succeeded and deleted runs stay in storage. Hangfire keeps them one day, which empties the Jobs page between weekly runs; failed runs never expire.</summary>
    public static readonly TimeSpan RunRetention = TimeSpan.FromDays(30);

    public static IServiceCollection AddObjeXBackgroundJobs(this IServiceCollection services, DatabaseOptions database)
    {
        // Per host: AddHangfire's default is the static JobStorage.Current, shared by every host in the process.
        services.AddSingleton<JobStorage>(_ =>
        {
            if (database.IsPostgreSql)
            {
                var options = new PostgreSqlStorageOptions();
                return new PostgreSqlStorage(new NpgsqlConnectionFactory(database.ConnectionString, options), options);
            }
            return new SQLiteStorage(database.SqliteFilePath!); // file path, not an EF connection string
        });
        services.AddHangfire((provider, config) => config
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseStorage(provider.GetRequiredService<JobStorage>())
            .WithJobExpirationTimeout(RunRetention));
        services.AddHangfireServer();
        services.AddSingleton<IJobMonitor, HangfireJobMonitor>();
        services.AddSingleton<IJobScheduler, HangfireJobScheduler>();

        services.AddScoped<CleanupOrphanedBlobsJob>();
        services.AddScoped<VerifyBlobIntegrityJob>();
        services.AddScoped<CleanupAbandonedMultipartJob>();
        services.AddScoped<RecountBucketStatsJob>();

        return services;
    }

    /// <summary>Declares the recurring jobs on their stored or default schedule and prunes recurring jobs this version does not declare.</summary>
    public static void RegisterRecurringJobs(IServiceProvider services)
    {
        var manager = services.GetRequiredService<IRecurringJobManager>();
        var storage = services.GetRequiredService<JobStorage>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(BackgroundJobs));
        var declared = JobDefinitions.All.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);

        using (var db = services.GetRequiredService<IDbContextFactory<ObjeXDbContext>>().CreateDbContext())
        {
            var schedules = db.JobSchedules.AsNoTracking().ToDictionary(s => s.JobId);
            foreach (var definition in JobDefinitions.All)
                HangfireJobScheduler.Apply(manager, definition, schedules.GetValueOrDefault(definition.Id), logger);
        }

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
    }
}
