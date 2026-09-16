using ObjeX.Core.Utilities;

namespace ObjeX.Web.Helpers;

/// <summary>Client configuration snippets; built in C# so Razor never parses the &lt;secret&gt; placeholder as a tag.</summary>
public static class S3ClientSnippets
{
    const string SecretPlaceholder = "<secret>";

    public static string AwsCli(string endpoint, string accessKeyId, string? secret) =>
        string.Join("\n",
            $"aws configure set aws_access_key_id {accessKeyId}",
            $"aws configure set aws_secret_access_key {secret ?? SecretPlaceholder}",
            $"aws configure set default.region {S3Conventions.Region}",
            "aws configure set default.s3.addressing_style path",
            $"aws --endpoint-url {Trim(endpoint)} s3 ls");

    public static string Rclone(string endpoint, string accessKeyId, string? secret) =>
        string.Join("\n",
            "[objex]",
            "type = s3",
            "provider = Other",
            $"access_key_id = {accessKeyId}",
            $"secret_access_key = {secret ?? SecretPlaceholder}",
            $"endpoint = {Trim(endpoint)}",
            $"region = {S3Conventions.Region}",
            "force_path_style = true");

    public static string Env(string endpoint, string accessKeyId, string? secret) =>
        string.Join("\n",
            $"export AWS_ACCESS_KEY_ID={accessKeyId}",
            $"export AWS_SECRET_ACCESS_KEY={secret ?? SecretPlaceholder}",
            $"export AWS_ENDPOINT_URL={Trim(endpoint)}",
            $"export AWS_DEFAULT_REGION={S3Conventions.Region}");

    static string Trim(string endpoint) => endpoint.TrimEnd('/');
}
