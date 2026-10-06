using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ObjeX.Api.Startup;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Tests.Integration;

/// <summary>
/// The trigram index behind object search on PostgreSQL, built by SearchIndexBuilder after start. The test factory removes
/// hosted services, so the tests run the builder themselves. On SQLite it is not registered and there is nothing to check.
/// </summary>
public class SearchIndexTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private const string Definition = "USING gin (lower(key) gin_trgm_ops)";

    private async Task<SearchIndexBuilder?> BuiltAsync()
    {
        var builder = factory.Services.GetService<SearchIndexBuilder>();
        if (builder is not null)
            await builder.EnsureAsync();
        return builder;
    }

    // The definition of a valid index, "invalid" for one an interrupted build left, null when there is none.
    private async Task<string?> IndexStateAsync()
    {
        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
        var rows = await db.Database.SqlQueryRaw<string>(
            "SELECT CASE WHEN i.indisvalid THEN pg_get_indexdef(i.indexrelid) ELSE 'invalid' END AS \"Value\" " +
            "FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid JOIN pg_namespace n ON n.oid = c.relnamespace " +
            "WHERE c.relname = 'ix_blob_objects_key_trgm' AND n.nspname = current_schema()").ToListAsync();
        return rows.SingleOrDefault();
    }

    private async Task ExecuteAsync(string sql)
    {
        using var scope = factory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ObjeXDbContext>().Database.ExecuteSqlRawAsync(sql);
    }

    [Fact]
    public async Task PostgreSql_BuildsTheIndexWhenTheHostStarts()
    {
        if (factory.Services.GetService<SearchIndexBuilder>() is not { } builder) return;
        await ExecuteAsync("DROP INDEX IF EXISTS ix_blob_objects_key_trgm");

        await builder.StartAsync(CancellationToken.None);
        await builder.ExecuteTask!;

        Assert.Contains(Definition, await IndexStateAsync());
    }

    [Fact]
    public async Task PostgreSql_RebuildsAnIndexThatAnInterruptedBuildLeftInvalid()
    {
        if (await BuiltAsync() is not { } builder) return;
        await ExecuteAsync("UPDATE pg_index SET indisvalid = false WHERE indexrelid = 'ix_blob_objects_key_trgm'::regclass");
        Assert.Equal("invalid", await IndexStateAsync());

        await builder.EnsureAsync();

        Assert.Contains(Definition, await IndexStateAsync());
    }

    [Fact]
    public async Task PostgreSql_WithoutPgTrgmRunsWithoutTheIndexAndBuildsItOnceInstalled()
    {
        if (await BuiltAsync() is not { } builder) return;
        await ExecuteAsync("DROP EXTENSION pg_trgm CASCADE");

        await builder.EnsureAsync();
        Assert.Null(await IndexStateAsync());

        await ExecuteAsync("CREATE EXTENSION pg_trgm");
        await builder.EnsureAsync();
        Assert.Contains(Definition, await IndexStateAsync());
    }
}
