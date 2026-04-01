using System.Net;
using System.Text;

namespace ObjeX.Tests.Integration;

public class S3BatchDeleteTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private readonly HttpClient _client = factory.CreateS3Client();

    [Fact]
    public async Task BatchDelete_MultipleKeys_AllDeleted()
    {
        var bucket = "test-bucket";
        var id = Guid.NewGuid().ToString("N")[..6];
        var keys = new[] { $"batch-{id}-a.txt", $"batch-{id}-b.txt", $"batch-{id}-c.txt" };

        // Upload 3 objects
        foreach (var key in keys)
        {
            var content = Encoding.UTF8.GetBytes($"content-{key}");
            var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/{key}");
            putRequest.Content = new ByteArrayContent(content);
            S3RequestSigner.SignRequest(putRequest, factory.AccessKeyId, factory.SecretAccessKey, content);
            await _client.SendAsync(putRequest);
        }

        // Batch delete
        var deleteXml = new StringBuilder("<Delete>");
        foreach (var key in keys)
            deleteXml.Append($"<Object><Key>{key}</Key></Object>");
        deleteXml.Append("</Delete>");

        var body = Encoding.UTF8.GetBytes(deleteXml.ToString());
        var deleteRequest = new HttpRequestMessage(HttpMethod.Post, $"/{bucket}?delete");
        deleteRequest.Content = new ByteArrayContent(body);
        deleteRequest.Content.Headers.ContentType = new("application/xml");
        S3RequestSigner.SignRequest(deleteRequest, factory.AccessKeyId, factory.SecretAccessKey, body);
        var deleteResponse = await _client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var responseXml = await deleteResponse.Content.ReadAsStringAsync();
        Assert.Contains("DeleteResult", responseXml);
        foreach (var key in keys)
            Assert.Contains(key, responseXml);

        // Verify all objects are gone
        foreach (var key in keys)
        {
            var headRequest = new HttpRequestMessage(HttpMethod.Head, $"/{bucket}/{key}");
            S3RequestSigner.SignRequest(headRequest, factory.AccessKeyId, factory.SecretAccessKey);
            var headResponse = await _client.SendAsync(headRequest);
            Assert.Equal(HttpStatusCode.NotFound, headResponse.StatusCode);
        }
    }

    [Fact]
    public async Task BatchDelete_MixExistingAndNonExistent_Succeeds()
    {
        var bucket = "test-bucket";
        var id = Guid.NewGuid().ToString("N")[..6];
        var existingKey = $"batch-mix-{id}.txt";
        var nonExistentKey = $"batch-ghost-{id}.txt";

        // Upload one object
        var content = "exists"u8.ToArray();
        var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/{existingKey}");
        putRequest.Content = new ByteArrayContent(content);
        S3RequestSigner.SignRequest(putRequest, factory.AccessKeyId, factory.SecretAccessKey, content);
        await _client.SendAsync(putRequest);

        // Batch delete both (one exists, one doesn't)
        var deleteXml = $"<Delete><Object><Key>{existingKey}</Key></Object><Object><Key>{nonExistentKey}</Key></Object></Delete>";
        var body = Encoding.UTF8.GetBytes(deleteXml);
        var deleteRequest = new HttpRequestMessage(HttpMethod.Post, $"/{bucket}?delete");
        deleteRequest.Content = new ByteArrayContent(body);
        deleteRequest.Content.Headers.ContentType = new("application/xml");
        S3RequestSigner.SignRequest(deleteRequest, factory.AccessKeyId, factory.SecretAccessKey, body);
        var response = await _client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
