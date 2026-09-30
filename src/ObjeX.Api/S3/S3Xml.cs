using System.Security;
using System.Text;
using ObjeX.Core.Models;
using ObjeX.Core.Utilities;

namespace ObjeX.Api.S3;

public static class S3Xml
{
    private static string Escape(string? value) =>
        SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;

    public static IResult ListBuckets(IEnumerable<Bucket> buckets)
    {
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.AppendLine("<ListAllMyBucketsResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">");
        xml.AppendLine("  <Owner><ID>owner</ID><DisplayName>owner</DisplayName></Owner>");
        xml.AppendLine("  <Buckets>");
        foreach (var bucket in buckets)
        {
            xml.AppendLine("    <Bucket>");
            xml.AppendLine($"      <Name>{Escape(bucket.Name)}</Name>");
            xml.AppendLine($"      <CreationDate>{bucket.CreatedAt:yyyy-MM-ddTHH:mm:ss.fffZ}</CreationDate>");
            xml.AppendLine("    </Bucket>");
        }
        xml.AppendLine("  </Buckets>");
        xml.AppendLine("</ListAllMyBucketsResult>");
        return Results.Content(xml.ToString(), "application/xml", Encoding.UTF8);
    }

    /// <summary>What a listing request asked for; shared by ListObjects, ListObjectsV2 and ListObjectVersions.</summary>
    public record Listing(string Bucket, string? Prefix, string? Delimiter, int MaxKeys, bool UrlEncode, string OwnerId, string OwnerName);

    public static IResult ListObjects(ListObjectsResult page, Listing listing, string? marker)
    {
        // botocore does not decode Prefix for ListObjects (it does for V2), so it goes out unencoded here.
        var xml = Begin("ListBucketResult", listing, encodePrefix: false);
        xml.AppendLine($"  <Marker>{Value(marker, listing)}</Marker>");
        if (page.IsTruncated && !string.IsNullOrEmpty(listing.Delimiter))
            xml.AppendLine($"  <NextMarker>{Value(page.NextMarker, listing)}</NextMarker>");
        xml.AppendLine($"  <IsTruncated>{Bool(page.IsTruncated)}</IsTruncated>");
        AppendEntries(xml, page, listing, "Contents", owner: true, version: false);
        return End(xml, "ListBucketResult");
    }

    public static IResult ListObjectsV2(ListObjectsResult page, Listing listing, string? continuationToken, string? startAfter, bool fetchOwner)
    {
        var xml = Begin("ListBucketResult", listing, encodePrefix: true);
        xml.AppendLine($"  <KeyCount>{page.Objects.Count() + page.CommonPrefixes.Count()}</KeyCount>");
        xml.AppendLine($"  <IsTruncated>{Bool(page.IsTruncated)}</IsTruncated>");
        if (continuationToken is not null)
            xml.AppendLine($"  <ContinuationToken>{Escape(continuationToken)}</ContinuationToken>");
        if (page.IsTruncated && page.NextMarker is not null)
            xml.AppendLine($"  <NextContinuationToken>{ContinuationToken.Encode(page.NextMarker)}</NextContinuationToken>");
        if (startAfter is not null)
            xml.AppendLine($"  <StartAfter>{Value(startAfter, listing)}</StartAfter>");
        AppendEntries(xml, page, listing, "Contents", owner: fetchOwner, version: false);
        return End(xml, "ListBucketResult");
    }

    public static IResult ListObjectVersions(ListObjectsResult page, Listing listing, string? keyMarker)
    {
        var xml = Begin("ListVersionsResult", listing, encodePrefix: true);
        xml.AppendLine($"  <KeyMarker>{Value(keyMarker, listing)}</KeyMarker>");
        xml.AppendLine("  <VersionIdMarker></VersionIdMarker>");
        if (page.IsTruncated)
        {
            xml.AppendLine($"  <NextKeyMarker>{Value(page.NextMarker, listing)}</NextKeyMarker>");
            xml.AppendLine("  <NextVersionIdMarker>null</NextVersionIdMarker>");
        }
        xml.AppendLine($"  <IsTruncated>{Bool(page.IsTruncated)}</IsTruncated>");
        AppendEntries(xml, page, listing, "Version", owner: true, version: true);
        return End(xml, "ListVersionsResult");
    }

    private static StringBuilder Begin(string root, Listing listing, bool encodePrefix)
    {
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.AppendLine($"<{root} xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">");
        xml.AppendLine($"  <Name>{Escape(listing.Bucket)}</Name>");
        xml.AppendLine($"  <Prefix>{(encodePrefix ? Value(listing.Prefix, listing) : Escape(listing.Prefix))}</Prefix>");
        if (!string.IsNullOrEmpty(listing.Delimiter))
            xml.AppendLine($"  <Delimiter>{Value(listing.Delimiter, listing)}</Delimiter>");
        xml.AppendLine($"  <MaxKeys>{listing.MaxKeys}</MaxKeys>");
        if (listing.UrlEncode)
            xml.AppendLine("  <EncodingType>url</EncodingType>");
        return xml;
    }

    private static void AppendEntries(StringBuilder xml, ListObjectsResult page, Listing listing, string element, bool owner, bool version)
    {
        foreach (var obj in page.Objects)
        {
            xml.AppendLine($"  <{element}>");
            xml.AppendLine($"    <Key>{Value(obj.Key, listing)}</Key>");
            if (version)
            {
                xml.AppendLine("    <VersionId>null</VersionId>");
                xml.AppendLine("    <IsLatest>true</IsLatest>");
            }
            xml.AppendLine($"    <LastModified>{obj.UpdatedAt:yyyy-MM-ddTHH:mm:ss.fffZ}</LastModified>");
            xml.AppendLine($"    <ETag>&quot;{Escape(obj.ETag)}&quot;</ETag>");
            xml.AppendLine($"    <Size>{obj.Size}</Size>");
            xml.AppendLine("    <StorageClass>STANDARD</StorageClass>");
            if (owner)
                xml.AppendLine($"    <Owner><ID>{Escape(listing.OwnerId)}</ID><DisplayName>{Escape(listing.OwnerName)}</DisplayName></Owner>");
            xml.AppendLine($"  </{element}>");
        }
        foreach (var cp in page.CommonPrefixes)
            xml.AppendLine($"  <CommonPrefixes><Prefix>{Value(cp, listing)}</Prefix></CommonPrefixes>");
    }

    private static IResult End(StringBuilder xml, string root)
    {
        xml.AppendLine($"</{root}>");
        return Results.Content(xml.ToString(), "application/xml", Encoding.UTF8);
    }

    // encoding-type=url percent-encodes each path segment and keeps the slashes, as AWS does.
    private static string Value(string? value, Listing listing) =>
        Escape(listing.UrlEncode && value is not null ? string.Join('/', value.Split('/').Select(Uri.EscapeDataString)) : value);

    private static string Bool(bool value) => value ? "true" : "false";

    public static IResult VersioningConfiguration() =>
        Results.Content("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<VersioningConfiguration xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\"/>",
            "application/xml", Encoding.UTF8);

    public static IResult Error(string code, string message, int statusCode = 400)
        => Results.Content(ErrorDocument(code, message), "application/xml", Encoding.UTF8, statusCode);

    /// <summary>Writes an S3 error document straight to the response. For middleware, which has no IResult.</summary>
    public static Task WriteErrorAsync(HttpContext context, string code, string message, int statusCode)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/xml";
        return context.Response.WriteAsync(ErrorDocument(code, message));
    }

    private static string ErrorDocument(string code, string message)
    {
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.AppendLine("<Error>");
        xml.AppendLine($"  <Code>{Escape(code)}</Code>");
        xml.AppendLine($"  <Message>{Escape(message)}</Message>");
        xml.AppendLine("</Error>");
        return xml.ToString();
    }

    public static IResult InitiateMultipartUpload(string bucket, string key, Guid uploadId)
    {
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.AppendLine("<InitiateMultipartUploadResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">");
        xml.AppendLine($"  <Bucket>{Escape(bucket)}</Bucket>");
        xml.AppendLine($"  <Key>{Escape(key)}</Key>");
        xml.AppendLine($"  <UploadId>{uploadId}</UploadId>");
        xml.AppendLine("</InitiateMultipartUploadResult>");
        return Results.Content(xml.ToString(), "application/xml", Encoding.UTF8);
    }

    public static IResult CompleteMultipartUpload(string bucket, string key, string location, string etag)
    {
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.AppendLine("<CompleteMultipartUploadResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">");
        xml.AppendLine($"  <Location>{Escape(location)}</Location>");
        xml.AppendLine($"  <Bucket>{Escape(bucket)}</Bucket>");
        xml.AppendLine($"  <Key>{Escape(key)}</Key>");
        xml.AppendLine($"  <ETag>&quot;{Escape(etag)}&quot;</ETag>");
        xml.AppendLine("</CompleteMultipartUploadResult>");
        return Results.Content(xml.ToString(), "application/xml", Encoding.UTF8);
    }

    public static IResult ListParts(string bucket, string key, Guid uploadId, IEnumerable<MultipartUploadPart> parts)
    {
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.AppendLine("<ListPartsResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">");
        xml.AppendLine($"  <Bucket>{Escape(bucket)}</Bucket>");
        xml.AppendLine($"  <Key>{Escape(key)}</Key>");
        xml.AppendLine($"  <UploadId>{uploadId}</UploadId>");
        xml.AppendLine("  <IsTruncated>false</IsTruncated>");
        foreach (var part in parts.OrderBy(p => p.PartNumber))
        {
            xml.AppendLine("  <Part>");
            xml.AppendLine($"    <PartNumber>{part.PartNumber}</PartNumber>");
            xml.AppendLine($"    <LastModified>{part.UpdatedAt:yyyy-MM-ddTHH:mm:ss.fffZ}</LastModified>");
            xml.AppendLine($"    <ETag>&quot;{Escape(part.ETag)}&quot;</ETag>");
            xml.AppendLine($"    <Size>{part.Size}</Size>");
            xml.AppendLine("  </Part>");
        }
        xml.AppendLine("</ListPartsResult>");
        return Results.Content(xml.ToString(), "application/xml", Encoding.UTF8);
    }

    public static IResult ListMultipartUploads(string bucket, IEnumerable<MultipartUpload> uploads)
    {
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.AppendLine("<ListMultipartUploadsResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">");
        xml.AppendLine($"  <Bucket>{Escape(bucket)}</Bucket>");
        xml.AppendLine("  <IsTruncated>false</IsTruncated>");
        foreach (var upload in uploads)
        {
            xml.AppendLine("  <Upload>");
            xml.AppendLine($"    <Key>{Escape(upload.Key)}</Key>");
            xml.AppendLine($"    <UploadId>{upload.Id}</UploadId>");
            xml.AppendLine($"    <Initiated>{upload.CreatedAt:yyyy-MM-ddTHH:mm:ss.fffZ}</Initiated>");
            xml.AppendLine("  </Upload>");
        }
        xml.AppendLine("</ListMultipartUploadsResult>");
        return Results.Content(xml.ToString(), "application/xml", Encoding.UTF8);
    }

    public static IResult CopyObjectResult(string etag, DateTime lastModified) => CopyResult("CopyObjectResult", etag, lastModified);

    public static IResult CopyPartResult(string etag, DateTime lastModified) => CopyResult("CopyPartResult", etag, lastModified);

    private static IResult CopyResult(string root, string etag, DateTime lastModified)
    {
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.AppendLine($"<{root} xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">");
        xml.AppendLine($"  <ETag>&quot;{Escape(etag)}&quot;</ETag>");
        xml.AppendLine($"  <LastModified>{lastModified:yyyy-MM-ddTHH:mm:ss.fffZ}</LastModified>");
        xml.AppendLine($"</{root}>");
        return Results.Content(xml.ToString(), "application/xml", Encoding.UTF8);
    }

    public static IResult BucketLocation()
    {
        return Results.Content(
            $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<LocationConstraint xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">{S3Conventions.Region}</LocationConstraint>",
            "application/xml", Encoding.UTF8);
    }

    public static IResult DeleteResult(List<string> deleted, List<(string Key, string Code, string Message)> errors)
    {
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.AppendLine("<DeleteResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">");
        foreach (var key in deleted)
        {
            xml.AppendLine("  <Deleted>");
            xml.AppendLine($"    <Key>{Escape(key)}</Key>");
            xml.AppendLine("  </Deleted>");
        }
        foreach (var (key, code, message) in errors)
        {
            xml.AppendLine("  <Error>");
            xml.AppendLine($"    <Key>{Escape(key)}</Key>");
            xml.AppendLine($"    <Code>{Escape(code)}</Code>");
            xml.AppendLine($"    <Message>{Escape(message)}</Message>");
            xml.AppendLine("  </Error>");
        }
        xml.AppendLine("</DeleteResult>");
        return Results.Content(xml.ToString(), "application/xml", Encoding.UTF8);
    }
}
