using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ObjeX.Core.Models;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Tests.Integration;

public class StorageQuotaTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private readonly HttpClient _client = factory.CreateS3Client();

    [Fact]
    public async Task UserWithQuota_UploadWithinLimit_Succeeds()
    {
        var (userId, accessKeyId, secretKey) = await CreateUserWithQuota("quota-ok", quotaBytes: 10_000);
        var bucket = await CreateBucketForUser(userId, "quota-ok-bucket");

        var content = new byte[500]; // well within 10KB quota

        var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/small.bin");
        putRequest.Content = new ByteArrayContent(content);
        S3RequestSigner.SignRequest(putRequest, accessKeyId, secretKey, content);
        var response = await _client.SendAsync(putRequest);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task UserWithQuota_UploadExceedingLimit_Returns507()
    {
        var (userId, accessKeyId, secretKey) = await CreateUserWithQuota("quota-exceed", quotaBytes: 100);
        var bucket = await CreateBucketForUser(userId, "quota-exceed-bucket");

        var content = new byte[2000]; // exceeds 100-byte quota

        var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/big.bin");
        putRequest.Content = new ByteArrayContent(content);
        putRequest.Content.Headers.ContentLength = content.Length;
        S3RequestSigner.SignRequest(putRequest, accessKeyId, secretKey, content);
        var response = await _client.SendAsync(putRequest);
        Assert.Equal((HttpStatusCode)507, response.StatusCode);
    }

    [Fact]
    public async Task AdminUser_IgnoresGlobalDefaultQuota()
    {
        // Set a tiny global default quota
        using (var scope = factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
            var settings = await db.SystemSettings.FindAsync(1);
            settings!.DefaultStorageQuotaBytes = 1; // 1 byte
            await db.SaveChangesAsync();
        }

        try
        {
            // Admin should bypass global default
            var content = new byte[1000];
            var putRequest = new HttpRequestMessage(HttpMethod.Put, "/test-bucket/admin-quota.bin");
            putRequest.Content = new ByteArrayContent(content);
            putRequest.Content.Headers.ContentLength = content.Length;
            S3RequestSigner.SignRequest(putRequest, factory.AccessKeyId, factory.SecretAccessKey, content);
            var response = await _client.SendAsync(putRequest);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        finally
        {
            // Reset global default
            using var scope = factory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
            var settings = await db.SystemSettings.FindAsync(1);
            settings!.DefaultStorageQuotaBytes = null;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task GlobalDefaultQuota_AppliedToUserRole()
    {
        var (userId, accessKeyId, secretKey) = await CreateUserWithQuota("quota-global", quotaBytes: null); // no per-user quota
        var bucket = await CreateBucketForUser(userId, "quota-global-bucket");

        // Set global default to 100 bytes
        using (var scope = factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
            var settings = await db.SystemSettings.FindAsync(1);
            settings!.DefaultStorageQuotaBytes = 100;
            await db.SaveChangesAsync();
        }

        try
        {
            var content = new byte[200]; // exceeds global 100-byte default
            var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/too-big.bin");
            putRequest.Content = new ByteArrayContent(content);
            putRequest.Content.Headers.ContentLength = content.Length;
            S3RequestSigner.SignRequest(putRequest, accessKeyId, secretKey, content);
            var response = await _client.SendAsync(putRequest);
            Assert.Equal((HttpStatusCode)507, response.StatusCode);
        }
        finally
        {
            using var scope = factory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
            var settings = await db.SystemSettings.FindAsync(1);
            settings!.DefaultStorageQuotaBytes = null;
            await db.SaveChangesAsync();
        }
    }

    private async Task<(string UserId, string AccessKeyId, string SecretKey)> CreateUserWithQuota(
        string username, long? quotaBytes)
    {
        using var scope = factory.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();

        var user = new User
        {
            UserName = username,
            Email = $"{username}@test.local",
            EmailConfirmed = true,
            StorageQuotaBytes = quotaBytes
        };
        var result = await userManager.CreateAsync(user, "test1234");
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
        await userManager.AddToRoleAsync(user, "User");

        var (credential, secret) = S3Credential.Create($"{username}-key", user.Id);
        db.S3Credentials.Add(credential);
        await db.SaveChangesAsync();

        return (user.Id, credential.AccessKeyId, secret);
    }

    private async Task<string> CreateBucketForUser(string userId, string bucketName)
    {
        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
        db.Buckets.Add(new Bucket { Name = bucketName, OwnerId = userId });
        await db.SaveChangesAsync();
        return bucketName;
    }
}
