using System.IO.Compression;
using ObjeX.Core.Interfaces;

namespace ObjeX.Api.Endpoints;

public static class DownloadEndpoints
{
    public static IEndpointConventionBuilder MapDownloadEndpoints(this WebApplication app)
    {
        return app.MapGet("/api/objects/{bucketName}/download", async (string bucketName, string? prefix, IMetadataService metadata, IObjectStorageService storage) =>
        {
            if (!await metadata.ExistsBucketAsync(bucketName))
                return Results.NotFound();

            var result = await metadata.ListObjectsAsync(bucketName, prefix, delimiter: null);
            var files = result.Objects.Where(o => !o.Key.EndsWith("/")).ToList();

            var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var obj in files)
                {
                    var entry = zip.CreateEntry(obj.Key, CompressionLevel.Fastest);
                    await using var fileStream = await storage.RetrieveAsync(bucketName, obj.Key);
                    await using var entryStream = entry.Open();
                    await fileStream.CopyToAsync(entryStream);
                }
            }
            ms.Position = 0;

            var zipName = string.IsNullOrEmpty(prefix) ? $"{bucketName}.zip" : $"{prefix.TrimEnd('/')}.zip";
            return Results.File(ms, "application/zip", zipName);
        });
    }
}
