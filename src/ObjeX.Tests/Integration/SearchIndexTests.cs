using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ObjeX.Api.Options;
using ObjeX.Api.Startup;
using ObjeX.Core.Interfaces;
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

    private async Task<SearchIndexState> StateAsync(ISearchIndex index) => (await index.GetStatusAsync()).State;

    [Fact]
    public async Task Sqlite_ReportsThatItUsesNoIndex()
    {
        if (factory.Services.GetService<SearchIndexBuilder>() is not null) return;

        Assert.Equal(SearchIndexState.NotUsed, await StateAsync(factory.Services.GetRequiredService<ISearchIndex>()));
    }

    [Fact]
    public async Task PostgreSql_ReportsAReadyIndexWithItsSize()
    {
        if (await BuiltAsync() is null) return;

        var status = await factory.Services.GetRequiredService<ISearchIndex>().GetStatusAsync();

        Assert.Equal(SearchIndexState.Ready, status.State);
        Assert.True(status.SizeBytes > 0);
    }

    [Fact]
    public async Task PostgreSql_SwitchedOffDropsTheIndexAndSaysSo()
    {
        if (await BuiltAsync() is not { } builder) return;
        var off = new SearchIndexBuilder(
            factory.Services.GetRequiredService<IDbContextFactory<ObjeXDbContext>>(),
            Microsoft.Extensions.Options.Options.Create(new SearchOptions { TrigramIndex = false }),
            NullLogger<SearchIndexBuilder>.Instance);

        var before = await off.GetStatusAsync();
        Assert.Equal(SearchIndexState.Dropping, before.State);
        Assert.True(before.SizeBytes > 0);

        await off.EnsureAsync();
        await off.EnsureAsync();

        Assert.Null(await IndexStateAsync());
        Assert.Equal(SearchIndexState.Off, await StateAsync(off));
        Assert.Equal(SearchIndexState.Missing, await StateAsync(builder));

        await builder.EnsureAsync();
        Assert.Contains(Definition, await IndexStateAsync());
    }

    [Fact]
    public async Task PostgreSql_ReadsTheSwitchFromConfiguration()
    {
        if (factory.Services.GetService<SearchIndexBuilder>() is null) return;
        using var off = factory.WithWebHostBuilder(b => b.UseSetting("Search:TrigramIndex", "false"));

        Assert.Contains(await StateAsync(off.Services.GetRequiredService<ISearchIndex>()), new[] { SearchIndexState.Off, SearchIndexState.Dropping });
        Assert.Equal(SearchIndexState.Ready, await StateAsync((await BuiltAsync())!));
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
        Assert.Equal(SearchIndexState.Missing, await StateAsync(builder));

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
        Assert.Equal(SearchIndexState.ExtensionMissing, await StateAsync(builder));

        await ExecuteAsync("CREATE EXTENSION pg_trgm");
        await builder.EnsureAsync();
        Assert.Contains(Definition, await IndexStateAsync());
    }
}
