using System.Security;
using System.Text;
using ObjeX.Core.Models;

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

    public static IResult ListObjects(string bucket, IEnumerable<BlobObject> objects, IEnumerable<string> commonPrefixes, string? prefix, string? delimiter)
    {
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.AppendLine("<ListBucketResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">");
        xml.AppendLine($"  <Name>{Escape(bucket)}</Name>");
        xml.AppendLine($"  <Prefix>{Escape(prefix)}</Prefix>");
        xml.AppendLine($"  <Delimiter>{Escape(delimiter)}</Delimiter>");
        xml.AppendLine("  <IsTruncated>false</IsTruncated>");
        foreach (var obj in objects.Where(o => !o.Key.EndsWith('/')))
        {
            xml.AppendLine("  <Contents>");
            xml.AppendLine($"    <Key>{Escape(obj.Key)}</Key>");
            xml.AppendLine($"    <LastModified>{obj.UpdatedAt:yyyy-MM-ddTHH:mm:ss.fffZ}</LastModified>");
            xml.AppendLine($"    <ETag>&quot;{Escape(obj.ETag)}&quot;</ETag>");
            xml.AppendLine($"    <Size>{obj.Size}</Size>");
            xml.AppendLine("    <StorageClass>STANDARD</StorageClass>");
            xml.AppendLine("  </Contents>");
        }
        foreach (var cp in commonPrefixes)
        {
            xml.AppendLine("  <CommonPrefixes>");
            xml.AppendLine($"    <Prefix>{Escape(cp)}</Prefix>");
            xml.AppendLine("  </CommonPrefixes>");
        }
        xml.AppendLine("</ListBucketResult>");
        return Results.Content(xml.ToString(), "application/xml", Encoding.UTF8);
    }

    public static IResult Error(string code, string message, int statusCode = 400)
    {
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.AppendLine("<Error>");
        xml.AppendLine($"  <Code>{Escape(code)}</Code>");
        xml.AppendLine($"  <Message>{Escape(message)}</Message>");
        xml.AppendLine("</Error>");
        return Results.Content(xml.ToString(), "application/xml", Encoding.UTF8, statusCode);
    }
}
