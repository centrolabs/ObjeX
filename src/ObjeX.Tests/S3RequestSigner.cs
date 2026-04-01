using System.Security.Cryptography;
using System.Text;

namespace ObjeX.Tests;

/// <summary>
/// Minimal AWS SigV4 header signer for test HTTP requests.
/// Produces the Authorization, x-amz-date, and x-amz-content-sha256 headers.
/// </summary>
public static class S3RequestSigner
{
    private const string Region = "us-east-1";
    private const string Service = "s3";

    public static void SignRequest(HttpRequestMessage request, string accessKeyId, string secretAccessKey, byte[]? body = null)
        => SignRequestWithTimestamp(request, accessKeyId, secretAccessKey, DateTime.UtcNow, body);

    public static void SignRequestWithTimestamp(
        HttpRequestMessage request, string accessKeyId, string secretAccessKey,
        DateTime timestamp, byte[]? body = null)
    {
        var payloadHash = body is { Length: > 0 }
            ? ToHex(SHA256.HashData(body))
            : "UNSIGNED-PAYLOAD";

        var date = timestamp.ToString("yyyyMMdd");
        var amzDate = timestamp.ToString("yyyyMMdd'T'HHmmss'Z'");
        var uri = request.RequestUri!;
        var host = request.Headers.Host
            ?? (uri.IsAbsoluteUri ? uri.Authority : "localhost:9000");

        // Ensure Host header is set on the request itself (not just DefaultRequestHeaders)
        request.Headers.Host ??= host;

        request.Headers.Remove("x-amz-date");
        request.Headers.Remove("x-amz-content-sha256");
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);

        var signedHeaders = "host;x-amz-content-sha256;x-amz-date";

        // Handle both absolute and relative URIs (TestServer uses relative)
        string rawPath;
        string rawQuery;
        if (uri.IsAbsoluteUri)
        {
            rawPath = uri.AbsolutePath;
            rawQuery = uri.Query;
        }
        else
        {
            var raw = uri.OriginalString;
            var qIdx = raw.IndexOf('?');
            rawPath = qIdx < 0 ? raw : raw[..qIdx];
            rawQuery = qIdx < 0 ? "" : raw[(qIdx + 1)..];
        }

        // Normalize canonical URI: decode then re-encode each segment
        // (matches server-side SigV4Signer.GetCanonicalUri behavior)
        var decoded = Uri.UnescapeDataString(rawPath);
        var canonicalUri = string.Join("/", decoded.Split('/').Select(UriEncode));
        var canonicalQuery = BuildCanonicalQueryString(rawQuery);

        var canonicalHeaders =
            $"host:{host}\n" +
            $"x-amz-content-sha256:{payloadHash}\n" +
            $"x-amz-date:{amzDate}\n";

        var canonicalRequest = string.Join("\n",
            request.Method.Method.ToUpperInvariant(),
            canonicalUri,
            canonicalQuery,
            canonicalHeaders,
            signedHeaders,
            payloadHash);

        var scope = $"{date}/{Region}/{Service}/aws4_request";
        var stringToSign = string.Join("\n",
            "AWS4-HMAC-SHA256",
            amzDate,
            scope,
            ToHex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest))));

        var signingKey = DeriveSigningKey(secretAccessKey, date);
        var signature = ToHex(HmacSha256(signingKey, Encoding.UTF8.GetBytes(stringToSign)));

        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "AWS4-HMAC-SHA256",
            $"Credential={accessKeyId}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}");
    }

    private static string BuildCanonicalQueryString(string query)
    {
        query = query.TrimStart('?');
        if (string.IsNullOrEmpty(query)) return "";

        var pairs = query.Split('&')
            .Select(p =>
            {
                var eq = p.IndexOf('=');
                return eq < 0
                    ? (Key: UriEncode(Uri.UnescapeDataString(p)), Value: "")
                    : (Key: UriEncode(Uri.UnescapeDataString(p[..eq])),
                       Value: UriEncode(Uri.UnescapeDataString(p[(eq + 1)..])));
            })
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .ThenBy(p => p.Value, StringComparer.Ordinal);

        return string.Join("&", pairs.Select(p => $"{p.Key}={p.Value}"));
    }

    private static byte[] DeriveSigningKey(string secret, string date)
    {
        var kDate = HmacSha256(Encoding.UTF8.GetBytes("AWS4" + secret), Encoding.UTF8.GetBytes(date));
        var kRegion = HmacSha256(kDate, Encoding.UTF8.GetBytes(Region));
        var kService = HmacSha256(kRegion, Encoding.UTF8.GetBytes(Service));
        return HmacSha256(kService, Encoding.UTF8.GetBytes("aws4_request"));
    }

    private static byte[] HmacSha256(byte[] key, byte[] data)
    {
        using var mac = new HMACSHA256(key);
        return mac.ComputeHash(data);
    }

    private static string ToHex(byte[] bytes) =>
        Convert.ToHexString(bytes).ToLowerInvariant();

    private static string UriEncode(string value)
    {
        var sb = new StringBuilder();
        foreach (var c in value)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '~')
                sb.Append(c);
            else
                foreach (var b in Encoding.UTF8.GetBytes(c.ToString()))
                    sb.Append($"%{b:X2}");
        }
        return sb.ToString();
    }
}
