using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Tests.Integration;

/// <summary>
/// Search spans every level below the current prefix, so it has to ignore folder placeholders.
/// Only "*" (any run of characters) and "?" (exactly one) are wildcards; LIKE's own "%", "_" and "\"
/// stay literal, so a key like "report-100%.txt" must not behave as a pattern.
/// </summary>
public class ObjectSearchTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private Task SeedAsync(string bucket, params string[] keys) => SeedAsync(bucket, null, keys);

    private async Task SeedAsync(string bucket, string? ownerId, string[] keys)
    {
        using var scope = factory.CreateScope();
        var metadata = scope.ServiceProvider.GetRequiredService<IMetadataService>();
        var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
        ownerId ??= await db.Users.Select(u => u.Id).FirstAsync();

        await metadata.CreateBucketAsync(new Bucket { Name = bucket, OwnerId = ownerId });
        foreach (var key in keys)
            await metadata.SaveObjectAsync(new BlobObject
            {
                BucketName = bucket,
                Key = key,
                ETag = "e",
                Size = 1,
                ContentType = key.EndsWith('/') ? "application/x-directory" : "application/octet-stream"
            });
    }

    private async Task<IReadOnlyList<string>> SearchAsync(string bucket, string? prefix, string term, int limit = 100)
    {
        using var scope = factory.CreateScope();
        var hits = await scope.ServiceProvider.GetRequiredService<IMetadataService>()
            .SearchObjectsAsync(bucket, prefix, term, limit);
        return hits.Select(o => o.Key).ToList();
    }

    [Fact]
    public async Task Term_MatchesAnywhereInTheKeyAcrossFolders()
    {
        await SeedAsync("search-across", "invoice.pdf", "2024/q1/invoice-final.pdf", "2024/q2/notes.txt", "archive/old-invoice.pdf");

        var hits = await SearchAsync("search-across", null, "invoice");

        Assert.Equal(["2024/q1/invoice-final.pdf", "archive/old-invoice.pdf", "invoice.pdf"], hits);
    }

    [Fact]
    public async Task Term_IsCaseInsensitive()
    {
        await SeedAsync("search-case", "Reports/Annual-REPORT.pdf", "notes.txt");

        Assert.Equal(["Reports/Annual-REPORT.pdf"], await SearchAsync("search-case", null, "annual-report"));
        Assert.Equal(["Reports/Annual-REPORT.pdf"], await SearchAsync("search-case", null, "REPORTS/"));
    }

    [Fact]
    public async Task Prefix_NarrowsTheScope()
    {
        await SeedAsync("search-prefix", "2024/q1/report.pdf", "2025/q1/report.pdf", "report.pdf");

        Assert.Equal(["2025/q1/report.pdf"], await SearchAsync("search-prefix", "2025/", "report"));
        Assert.Equal(["2024/q1/report.pdf", "2025/q1/report.pdf", "report.pdf"], await SearchAsync("search-prefix", "", "report"));
    }

    [Fact]
    public async Task WildcardCharactersInTheTermAreLiteral()
    {
        await SeedAsync("search-wildcards", "report-100%.txt", "report-1000.txt", "a_b.txt", "acb.txt", @"back\slash.txt", "backslash.txt");

        Assert.Equal(["report-100%.txt"], await SearchAsync("search-wildcards", null, "100%"));
        Assert.Equal(["a_b.txt"], await SearchAsync("search-wildcards", null, "a_b"));
        Assert.Equal([@"back\slash.txt"], await SearchAsync("search-wildcards", null, @"back\s"));
    }

    [Fact]
    public async Task Star_MatchesAnyRunAndAnchorsAtTheEnd()
    {
        await SeedAsync("search-star", "invoice.pdf", "2024/q1/report.pdf", "notes.txt", "x.pdfx");

        Assert.Equal(["2024/q1/report.pdf", "invoice.pdf"], await SearchAsync("search-star", null, "*.pdf"));
        Assert.Equal(["2024/q1/report.pdf"], await SearchAsync("search-star", null, "q1/*.pdf"));
    }

    [Fact]
    public async Task QuestionMark_MatchesExactlyOneCharacter()
    {
        await SeedAsync("search-single", "img_0001.jpg", "img_00001.jpg", "img_001.jpg");

        Assert.Equal(["img_0001.jpg"], await SearchAsync("search-single", null, "img_????.jpg"));
    }

    [Fact]
    public async Task LiteralPercentStaysLiteralNextToAStar()
    {
        await SeedAsync("search-mixed", "report-100%.txt", "report-1000.txt", "report-100%.csv");

        Assert.Equal(["report-100%.txt"], await SearchAsync("search-mixed", null, "100%*.txt"));
    }

    [Fact]
    public async Task PlaceholderObjectsAreExcluded()
    {
        await SeedAsync("search-placeholder", "docs/", "docs/manual.pdf");

        Assert.Equal(["docs/manual.pdf"], await SearchAsync("search-placeholder", null, "docs"));
    }

    [Fact]
    public async Task LimitIsRespectedAndOrderIsByteOrder()
    {
        await SeedAsync("search-limit", "b.txt", "B.txt", "a.txt", "Z.txt");

        Assert.Equal(["B.txt", "Z.txt"], await SearchAsync("search-limit", null, ".txt", limit: 2));
        Assert.Equal(["B.txt", "Z.txt", "a.txt", "b.txt"], await SearchAsync("search-limit", null, ".txt", limit: 10));
    }

    [Fact]
    public async Task WhitespaceTermReturnsEmpty()
    {
        await SeedAsync("search-blank", "a.txt", "b.txt");

        Assert.Empty(await SearchAsync("search-blank", null, "   "));
        Assert.Empty(await SearchAsync("search-blank", null, ""));
    }

    [Fact]
    public async Task Term_MatchesComposedAndDecomposedSpellings()
    {
        const string composed = "caf\u00e9-menu.txt";
        const string decomposed = "cafe\u0301-plan.txt";
        await SeedAsync("search-unicode", composed, decomposed, "cafe.txt");

        Assert.Equal([decomposed, composed], await SearchAsync("search-unicode", null, "caf\u00e9"));
        Assert.Equal([decomposed, composed], await SearchAsync("search-unicode", null, "cafe\u0301"));
        Assert.Equal([("search-unicode", decomposed), ("search-unicode", composed)], await SearchAllAsync(null, "caf\u00e9-"));
    }

    private async Task<string> CreateUserAsync(string username)
    {
        using var scope = factory.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = new User { UserName = username, Email = $"{username}@test.local", EmailConfirmed = true };
        Assert.True((await userManager.CreateAsync(user, "test1234")).Succeeded);
        return user.Id;
    }

    private async Task<IReadOnlyList<(string Bucket, string Key)>> SearchAllAsync(string? ownerFilter, string term, int limit = 100)
    {
        using var scope = factory.CreateScope();
        var hits = await scope.ServiceProvider.GetRequiredService<IMetadataService>()
            .SearchAllObjectsAsync(ownerFilter, term, limit);
        return hits.Select(o => (o.BucketName, o.Key)).ToList();
    }

    [Fact]
    public async Task SearchAll_OwnerFilterRestrictsToTheOwnersBuckets()
    {
        var ownerId = await CreateUserAsync("ledger-owner");
        var otherId = await CreateUserAsync("ledger-other");
        // Bucket order inverts key order, so the result proves the sort is bucket first.
        await SeedAsync("zz-ledger", ownerId, ["a-ledger.txt"]);
        await SeedAsync("aa-ledger", otherId, ["z-ledger.txt"]);

        Assert.Equal([("zz-ledger", "a-ledger.txt")], await SearchAllAsync(ownerId, "ledger"));
        Assert.Equal([("aa-ledger", "z-ledger.txt"), ("zz-ledger", "a-ledger.txt")], await SearchAllAsync(null, "ledger"));
    }

    private async Task<IReadOnlyList<string>> BestFirstAsync(string term)
    {
        using var scope = factory.CreateScope();
        var hits = await scope.ServiceProvider.GetRequiredService<IMetadataService>()
            .SearchAllObjectsAsync(null, term, 100, bestFirst: true);
        return hits.Select(o => o.Key).ToList();
    }

    [Fact]
    public async Task BestFirst_RanksASegmentStartFirstThenShorterKeysThenByteOrder()
    {
        await SeedAsync("rank-quokka", "x/my-quokka.txt", "quokka-notes/a.txt", "2024/quokka.txt", "c/quokka.txt", "b/quokka.txt");

        Assert.Equal(["b/quokka.txt", "c/quokka.txt", "2024/quokka.txt", "quokka-notes/a.txt", "x/my-quokka.txt"], await BestFirstAsync("Quokka"));
    }

    [Fact]
    public async Task BestFirst_RanksAWildcardTermByLengthOnly()
    {
        await SeedAsync("rank-wildcard", "x/my-wombat.txt", "wombat-notes/a.txt", "2024/wombat.txt", "b/wombat.txt");

        Assert.Equal(["b/wombat.txt", "2024/wombat.txt", "x/my-wombat.txt"], await BestFirstAsync("*wombat.txt"));
    }

    [Fact]
    public async Task BestFirst_RanksASegmentStartInEitherUnicodeForm()
    {
        const string decomposedStart = "cafe\u0301-numbat/menu.txt";
        const string composedInside = "x-caf\u00e9-numbat.txt";
        await SeedAsync("rank-unicode", decomposedStart, composedInside);

        Assert.Equal([decomposedStart, composedInside], await BestFirstAsync("caf\u00e9-numbat"));
    }

    [Fact]
    public async Task PostgreSql_IndexesTheLowerCasedKeyWithTrigrams()
    {
        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
        // SQLite scans; the index exists only on PostgreSQL.
        if (db.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL") return;

        var definition = await db.Database
            .SqlQueryRaw<string>("SELECT indexdef AS \"Value\" FROM pg_indexes WHERE indexname = 'ix_blob_objects_key_trgm'")
            .SingleAsync();

        Assert.Contains("USING gin (lower(key) gin_trgm_ops)", definition);
    }

    [Fact]
    public async Task SearchAll_UsesTheSameTermSemanticsAndSkipsPlaceholders()
    {
        await SeedAsync("all-semantics", "docs/", "docs/manual-x.pdf", "manual-x.pdfx");

        Assert.Equal([("all-semantics", "docs/manual-x.pdf")], await SearchAllAsync(null, "*manual-x.pdf"));
    }
}
