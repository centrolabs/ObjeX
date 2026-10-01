using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ObjeX.Core.Models;
using ObjeX.Infrastructure.Data;
using ObjeX.Infrastructure.Jobs;

namespace ObjeX.Tests.Integration;

/// <summary>The jobs read their setting from SystemSettings when they run; null means the default.</summary>
public class JobParameterTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private async Task SetAsync(int? graceMinutes, int? abandonedDays)
    {
        await using var db = await factory.Services.GetRequiredService<IDbContextFactory<ObjeXDbContext>>().CreateDbContextAsync();
        await db.SystemSettings.ExecuteUpdateAsync(s => s
            .SetProperty(x => x.OrphanGraceMinutes, graceMinutes)
            .SetProperty(x => x.AbandonedMultipartDays, abandonedDays));
    }

    private async Task<T> RunAsync<T>(Func<IServiceProvider, Task<T>> run)
    {
        using var scope = factory.CreateScope();
        return await run(scope.ServiceProvider);
    }

    [Fact]
    public async Task OrphanCleanup_KeepsFilesYoungerThanTheGrace_StoredOrDefault()
    {
        var file = Path.Combine(factory.BlobBasePath, "test-bucket", "ee", "ee", new string('e', 64) + ".blob");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllBytesAsync(file, "orphan"u8.ToArray());
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-30));

        await SetAsync(graceMinutes: null, abandonedDays: null);
        await RunAsync(sp => sp.GetRequiredService<CleanupOrphanedBlobsJob>().ExecuteAsync());
        Assert.True(File.Exists(file), "30 minutes is within the default grace of one hour");

        await SetAsync(graceMinutes: 15, abandonedDays: null);
        await RunAsync(sp => sp.GetRequiredService<CleanupOrphanedBlobsJob>().ExecuteAsync());
        Assert.False(File.Exists(file), "30 minutes is past a grace of 15 minutes");
    }

    [Fact]
    public async Task AbandonedMultipartCleanup_DeletesUploadsOlderThanTheAge_StoredOrDefault()
    {
        var upload = new MultipartUpload
        {
            BucketName = "test-bucket", Key = "parameter/upload.bin", ContentType = "application/octet-stream",
            InitiatedByUserId = "test", CreatedAt = DateTime.UtcNow.AddDays(-3),
        };
        await using (var db = await factory.Services.GetRequiredService<IDbContextFactory<ObjeXDbContext>>().CreateDbContextAsync())
        {
            db.MultipartUploads.Add(upload);
            await db.SaveChangesAsync();
        }

        await SetAsync(graceMinutes: null, abandonedDays: null);
        var kept = await RunAsync(sp => sp.GetRequiredService<CleanupAbandonedMultipartJob>().ExecuteAsync());
        Assert.Equal(0, kept.UploadsDeleted);

        await SetAsync(graceMinutes: null, abandonedDays: 2);
        var deleted = await RunAsync(sp => sp.GetRequiredService<CleanupAbandonedMultipartJob>().ExecuteAsync());
        Assert.Equal(1, deleted.UploadsDeleted);
    }
}
