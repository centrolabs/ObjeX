using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Tests.Integration;

public class StorageQuotaServiceTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private async Task<string> CreateUserAsync(string username, string role, long? quota, params long[] bucketSizes)
    {
        using var scope = factory.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();

        var user = new User { UserName = username, Email = $"{username}@test.local", EmailConfirmed = true, StorageQuotaBytes = quota };
        Assert.True((await userManager.CreateAsync(user, "test1234")).Succeeded);
        await userManager.AddToRoleAsync(user, role);

        var i = 0;
        foreach (var size in bucketSizes)
            db.Buckets.Add(new Bucket { Name = $"{username}-b{i++}", OwnerId = user.Id, TotalSize = size });

        var settings = await db.SystemSettings.FindAsync(1) ?? db.SystemSettings.Add(new SystemSettings { Id = 1 }).Entity;
        settings.DefaultStorageQuotaBytes = 1000;
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<StorageQuotaStatus> GetAsync(string userId)
    {
        using var scope = factory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IStorageQuotaService>().GetAsync(userId);
    }

    [Fact]
    public async Task UserRole_WithoutOwnQuota_GetsGlobalDefault_AndSumOfOwnedBuckets()
    {
        var id = await CreateUserAsync("quota-user-default", "User", null, 300, 200);

        var status = await GetAsync(id);

        Assert.Equal(500, status.UsedBytes);
        Assert.Equal(1000, status.QuotaBytes);
        Assert.Equal(50, status.UsedPercent);
    }

    [Fact]
    public async Task OwnQuota_WinsOverGlobalDefault_ForAnyRole()
    {
        var user = await CreateUserAsync("quota-user-own", "User", 50);
        var manager = await CreateUserAsync("quota-manager-own", "Manager", 70);

        Assert.Equal(50, (await GetAsync(user)).QuotaBytes);
        Assert.Equal(70, (await GetAsync(manager)).QuotaBytes);
    }

    private async Task<QuotaExceeded?> CheckWriteAsync(string bucket, string key, long newSize)
    {
        using var scope = factory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IStorageQuotaService>().CheckWriteAsync(bucket, key, newSize);
    }

    private async Task AddObjectAsync(string bucket, string key, long size)
    {
        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
        db.BlobObjects.Add(new BlobObject { BucketName = bucket, Key = key, Size = size, ETag = "e" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task CheckWrite_NewKey_ChargesTheFullSizeToTheBucketOwner()
    {
        await CreateUserAsync("check-new", "User", 1000, 600);

        Assert.Null(await CheckWriteAsync("check-new-b0", "fits.bin", 400));
        Assert.Equal(new QuotaExceeded(1001, 1000), await CheckWriteAsync("check-new-b0", "too-big.bin", 401));
    }

    [Fact]
    public async Task CheckWrite_Overwrite_ChargesOnlyTheGrowth()
    {
        await CreateUserAsync("check-overwrite", "User", 1000, 900);
        await AddObjectAsync("check-overwrite-b0", "same.bin", 300);

        Assert.Null(await CheckWriteAsync("check-overwrite-b0", "same.bin", 400));
        Assert.Null(await CheckWriteAsync("check-overwrite-b0", "same.bin", 10));
        Assert.Equal(new QuotaExceeded(1001, 1000), await CheckWriteAsync("check-overwrite-b0", "same.bin", 401));
    }

    [Fact]
    public async Task CheckWrite_UnlimitedOwnerOrUnknownBucket_PassesAnySize()
    {
        await CreateUserAsync("check-unlimited", "Manager", null, 900);

        Assert.Null(await CheckWriteAsync("check-unlimited-b0", "huge.bin", long.MaxValue / 2));
        Assert.Null(await CheckWriteAsync("no-such-bucket", "huge.bin", long.MaxValue / 2));
    }

    [Fact]
    public async Task PrivilegedRole_WithoutOwnQuota_IsUnlimited()
    {
        var id = await CreateUserAsync("quota-manager-unlimited", "Manager", null, 900);

        var status = await GetAsync(id);

        Assert.Equal(900, status.UsedBytes);
        Assert.Null(status.QuotaBytes);
        Assert.False(status.HasQuota);
    }
}
