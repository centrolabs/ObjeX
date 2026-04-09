using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Tests.Integration;

public class AuditLogTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private readonly HttpClient _client = factory.CreateS3Client();

    [Fact]
    public async Task CreateBucket_WritesAuditEntry()
    {
        var bucket = "audit-create-" + Guid.NewGuid().ToString("N")[..6] + "xx";

        var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}");
        S3RequestSigner.SignRequest(putRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var response = await _client.SendAsync(putRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
        var entry = await db.AuditEntries
            .Where(e => e.BucketName == bucket)
            .FirstOrDefaultAsync();
        Assert.NotNull(entry);
        Assert.Contains("Create", entry.Action, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadObject_WritesAuditEntry()
    {
        var bucket = "test-bucket";
        var key = "audit-upload-" + Guid.NewGuid().ToString("N")[..8] + ".txt";
        var content = "audit test"u8.ToArray();

        var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/{key}");
        putRequest.Content = new ByteArrayContent(content);
        S3RequestSigner.SignRequest(putRequest, factory.AccessKeyId, factory.SecretAccessKey, content);
        var response = await _client.SendAsync(putRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
        var entry = await db.AuditEntries
            .Where(e => e.BucketName == bucket && e.Key == key)
            .FirstOrDefaultAsync();
        Assert.NotNull(entry);
    }

    [Fact]
    public async Task DeleteObject_WritesAuditEntry()
    {
        var bucket = "test-bucket";
        var key = "audit-delete-" + Guid.NewGuid().ToString("N")[..8] + ".txt";
        var content = "will be deleted"u8.ToArray();

        // Upload first
        var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/{key}");
        putRequest.Content = new ByteArrayContent(content);
        S3RequestSigner.SignRequest(putRequest, factory.AccessKeyId, factory.SecretAccessKey, content);
        await _client.SendAsync(putRequest);

        // Delete
        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/{bucket}/{key}");
        S3RequestSigner.SignRequest(deleteRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var response = await _client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
        var entry = await db.AuditEntries
            .Where(e => e.BucketName == bucket && e.Key == key)
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefaultAsync();
        Assert.NotNull(entry);
        Assert.Contains("Delete", entry.Action, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteBucket_WritesAuditEntry()
    {
        var bucket = "audit-del-bucket-" + Guid.NewGuid().ToString("N")[..6] + "xx";

        // Create
        var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}");
        S3RequestSigner.SignRequest(putRequest, factory.AccessKeyId, factory.SecretAccessKey);
        await _client.SendAsync(putRequest);

        // Delete
        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/{bucket}");
        S3RequestSigner.SignRequest(deleteRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var response = await _client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
        var entries = await db.AuditEntries
            .Where(e => e.BucketName == bucket)
            .ToListAsync();
        Assert.Contains(entries, e => e.Action.Contains("Delete", StringComparison.OrdinalIgnoreCase));
    }
}
