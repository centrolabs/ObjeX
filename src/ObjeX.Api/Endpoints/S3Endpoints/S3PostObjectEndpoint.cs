using System.Security.Claims;
using System.Text.Json;
using System.Xml.Linq;

using ObjeX.Api.S3;
using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Core.Utilities;
using ObjeX.Core.Validation;
using ObjeX.Infrastructure.Storage;

namespace ObjeX.Api.Endpoints.S3Endpoints;

public static class S3PostObjectEndpoint
{
    static string GetCallerId(HttpContext ctx) =>
        ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    static bool IsPrivileged(HttpContext ctx) =>
        ctx.User.IsInRole("Admin") || ctx.User.IsInRole("Manager");

    public static void MapS3PostObjectEndpoints(this WebApplication app, RouteGroupBuilder s3)
    {
        // POST /{bucket} dispatches: ?delete → batch delete, otherwise → POST Object upload
        s3.MapPost("/{bucket}", async (string bucket, HttpRequest request, HttpContext ctx,
            IConfiguration config, IMetadataService metadata, IObjectStorageService storage,
            FileSystemStorageService fs) =>
        {
            if (request.Query.ContainsKey("delete") || request.QueryString.Value?.Contains("delete") == true)
                return await HandleDeleteObjects(bucket, request, ctx, metadata, storage);
            return await HandlePostObject(bucket, request, ctx, config, metadata, storage, fs);
        }).DisableAntiforgery();

        // POST / (bucketEndpoint mode: bucket is in form fields, not in URL)
        s3.MapPost("/", (HttpRequest request, HttpContext ctx,
            IConfiguration config, IMetadataService metadata, IObjectStorageService storage,
            FileSystemStorageService fs) => HandlePostObject(null, request, ctx, config, metadata, storage, fs))
            .DisableAntiforgery();
    }

    private static async Task<IResult> HandlePostObject(
        string? bucket,
        HttpRequest request,
        HttpContext ctx,
        IConfiguration config,
        IMetadataService metadata,
        IObjectStorageService storage,
        FileSystemStorageService fs)
    {
        if (!request.HasFormContentType)
            return S3Xml.Error(S3Errors.InvalidArgument, "POST Object requires multipart/form-data.");

        var form = await request.ReadFormAsync(ctx.RequestAborted);

        var key = form["key"].ToString();
        var policyB64 = form["policy"].ToString();

        if (string.IsNullOrEmpty(key))
            return S3Xml.Error(S3Errors.InvalidArgument, "Missing required form field: key.");

        if (string.IsNullOrEmpty(policyB64))
            return S3Xml.Error(S3Errors.AccessDenied, "POST Object requires a policy document.", 403);

        // Resolve bucket: from route param or form field
        if (string.IsNullOrEmpty(bucket))
            bucket = form["bucket"].ToString();
        if (string.IsNullOrEmpty(bucket))
            return S3Xml.Error(S3Errors.InvalidArgument, "Could not determine bucket name. Provide it in the URL or as a 'bucket' form field.");

        var policyError = ValidatePolicy(policyB64, bucket, key, form);
        if (policyError is not null)
            return S3Xml.Error(S3Errors.AccessDenied, policyError, 403);

        if (ObjectKeyValidator.GetValidationError(key) is { } keyError)
            return S3Xml.Error(S3Errors.InvalidArgument, keyError);

        if (await metadata.GetBucketAsync(bucket, IsPrivileged(ctx) ? null : GetCallerId(ctx)) is null)
            return S3Xml.Error(S3Errors.NoSuchBucket, "The specified bucket does not exist.", 404);

        var minFreeBytes = config.GetValue<long>("Storage:MinimumFreeDiskBytes", 500 * 1024 * 1024);
        if (fs.GetAvailableFreeSpace() < minFreeBytes)
            return S3Xml.Error(S3Errors.EntityTooLarge, "Insufficient disk space.", 507);

        var file = form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
            return S3Xml.Error(S3Errors.InvalidArgument, "No file provided.");

        var contentType = form["Content-Type"].ToString();
        if (string.IsNullOrEmpty(contentType))
            contentType = file.ContentType ?? "application/octet-stream";

        // Extract x-amz-meta-* from form fields
        var meta = new Dictionary<string, string>();
        foreach (var field in form.Where(f => f.Key.StartsWith("x-amz-meta-", StringComparison.OrdinalIgnoreCase)))
            meta[field.Key.ToLowerInvariant()] = field.Value.ToString();
        var customMetadata = meta.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(meta) : null;

        await using var fileStream = file.OpenReadStream();
        await using var hashingStream = new HashingStream(fileStream);
        var storagePath = await storage.StoreAsync(bucket, key, hashingStream);
        var size = await storage.GetSizeAsync(bucket, key);
        var etag = hashingStream.GetETag();

        await metadata.SaveObjectAsync(new BlobObject
        {
            BucketName = bucket,
            Key = key,
            Size = size,
            ContentType = contentType,
            ETag = etag,
            StoragePath = storagePath,
            CustomMetadata = customMetadata
        });

        var s3PublicUrl = config["S3:PublicUrl"] ?? "http://localhost:9000";
        ctx.Response.Headers.ETag = $"\"{etag}\"";
        ctx.Response.Headers.Location = $"{s3PublicUrl}/{bucket}/{key}";
        return Results.StatusCode(204);
    }

    private static async Task<IResult> HandleDeleteObjects(
        string bucket,
        HttpRequest request,
        HttpContext ctx,
        IMetadataService metadata,
        IObjectStorageService storage)
    {
        if (await metadata.GetBucketAsync(bucket, IsPrivileged(ctx) ? null : GetCallerId(ctx)) is null)
            return S3Xml.Error(S3Errors.NoSuchBucket, "The specified bucket does not exist.", 404);

        XDocument doc;
        try { doc = await XDocument.LoadAsync(request.Body, LoadOptions.None, ctx.RequestAborted); }
        catch { return S3Xml.Error(S3Errors.MalformedXML, "The XML you provided was not well-formed."); }

        var keys = doc.Descendants()
            .Where(e => e.Name.LocalName == "Key")
            .Select(e => e.Value)
            .Where(k => !string.IsNullOrEmpty(k))
            .ToList();

        if (keys.Count == 0)
            return S3Xml.Error(S3Errors.MalformedXML, "No keys specified.");

        var deleted = new List<string>();
        var errors = new List<(string Key, string Code, string Message)>();

        foreach (var key in keys)
        {
            try
            {
                if (await metadata.ExistsObjectAsync(bucket, key))
                {
                    await storage.DeleteAsync(bucket, key);
                    await metadata.DeleteObjectAsync(bucket, key);
                }
                deleted.Add(key);
            }
            catch (Exception ex)
            {
                errors.Add((key, S3Errors.InternalError, ex.Message));
            }
        }

        return S3Xml.DeleteResult(deleted, errors);
    }

    private static string? ValidatePolicy(string policyB64, string bucket, string key, IFormCollection form)
    {
        byte[] policyBytes;
        try { policyBytes = Convert.FromBase64String(policyB64); }
        catch { return "Invalid policy encoding."; }

        using var policy = JsonDocument.Parse(policyBytes);

        if (policy.RootElement.TryGetProperty("expiration", out var expProp))
        {
            if (DateTime.TryParse(expProp.GetString(), null,
                    System.Globalization.DateTimeStyles.AdjustToUniversal, out var expiration))
            {
                if (DateTime.UtcNow > expiration)
                    return "Policy has expired.";
            }
        }

        if (!policy.RootElement.TryGetProperty("conditions", out var conditions))
            return "Policy missing conditions.";

        foreach (var condition in conditions.EnumerateArray())
        {
            if (condition.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in condition.EnumerateObject())
                {
                    var fieldName = prop.Name;
                    var expected = prop.Value.GetString() ?? "";

                    var actual = fieldName.Equals("bucket", StringComparison.OrdinalIgnoreCase)
                        ? bucket
                        : fieldName.Equals("key", StringComparison.OrdinalIgnoreCase)
                            ? key
                            : form[fieldName].ToString();

                    if (!string.Equals(actual, expected, StringComparison.Ordinal))
                        return $"Policy condition not met for '{fieldName}'.";
                }
            }
            else if (condition.ValueKind == JsonValueKind.Array)
            {
                var items = condition.EnumerateArray().ToList();
                if (items.Count < 2) continue;

                var op = items[0].GetString() ?? "";

                if (op.Equals("content-length-range", StringComparison.OrdinalIgnoreCase) && items.Count >= 3)
                {
                    var min = items[1].GetInt64();
                    var max = items[2].GetInt64();
                    var f = form.Files.FirstOrDefault();
                    if (f is not null && (f.Length < min || f.Length > max))
                        return $"File size {f.Length} outside allowed range [{min}, {max}].";
                    continue;
                }

                if (items.Count < 3) continue;
                var fieldRef = items[1].GetString() ?? "";
                var value = items[2].GetString() ?? "";

                var field = fieldRef.StartsWith('$') ? fieldRef[1..] : fieldRef;
                var actual = field.Equals("bucket", StringComparison.OrdinalIgnoreCase)
                    ? bucket
                    : field.Equals("key", StringComparison.OrdinalIgnoreCase)
                        ? key
                        : form[field].ToString();

                if (op.Equals("eq", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(actual, value, StringComparison.Ordinal))
                        return $"Policy condition not met: {field} must equal '{value}'.";
                }
                else if (op.Equals("starts-with", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(value) && !actual.StartsWith(value, StringComparison.Ordinal))
                        return $"Policy condition not met: {field} must start with '{value}'.";
                }
            }
        }

        return null;
    }
}
