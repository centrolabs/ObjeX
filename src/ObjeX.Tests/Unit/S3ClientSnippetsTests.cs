using ObjeX.Web.Helpers;

namespace ObjeX.Tests.Unit;

public class S3ClientSnippetsTests
{
    const string Endpoint = "https://s3.example.com";
    const string Key = "OBXTESTACCESSKEY1234";

    public static TheoryData<string> AllSnippets =>
    [
        S3ClientSnippets.AwsCli(Endpoint, Key, null),
        S3ClientSnippets.Rclone(Endpoint, Key, null),
        S3ClientSnippets.Env(Endpoint, Key, null),
    ];

    [Theory]
    [MemberData(nameof(AllSnippets))]
    public void EverySnippet_ContainsEndpointKeyAndRegion(string snippet)
    {
        Assert.Contains(Endpoint, snippet);
        Assert.Contains(Key, snippet);
        Assert.Contains("us-east-1", snippet);
    }

    [Theory]
    [MemberData(nameof(AllSnippets))]
    public void EverySnippet_UsesPlaceholder_WhenSecretIsNull(string snippet)
        => Assert.Contains("<secret>", snippet);

    [Theory]
    [InlineData("cli")]
    [InlineData("rclone")]
    [InlineData("env")]
    public void EverySnippet_InsertsRealSecret_WhenGiven(string which)
    {
        var snippet = which switch
        {
            "cli"    => S3ClientSnippets.AwsCli(Endpoint, Key, "s3cr3t"),
            "rclone" => S3ClientSnippets.Rclone(Endpoint, Key, "s3cr3t"),
            _        => S3ClientSnippets.Env(Endpoint, Key, "s3cr3t"),
        };
        Assert.Contains("s3cr3t", snippet);
        Assert.DoesNotContain("<secret>", snippet);
    }

    [Theory]
    [InlineData("cli")]
    [InlineData("rclone")]
    [InlineData("env")]
    public void EverySnippet_StripsTrailingSlashFromEndpoint(string which)
    {
        var snippet = which switch
        {
            "cli"    => S3ClientSnippets.AwsCli(Endpoint + "/", Key, null),
            "rclone" => S3ClientSnippets.Rclone(Endpoint + "/", Key, null),
            _        => S3ClientSnippets.Env(Endpoint + "/", Key, null),
        };
        Assert.Contains(Endpoint, snippet);
        Assert.DoesNotContain(Endpoint + "/", snippet);
    }

    [Fact]
    public void AwsCli_ConfiguresPathAddressingAndEndpointUrl()
    {
        var snippet = S3ClientSnippets.AwsCli(Endpoint, Key, null);
        Assert.Contains("aws configure set default.s3.addressing_style path", snippet);
        Assert.Contains($"aws --endpoint-url {Endpoint} s3 ls", snippet);
    }

    [Fact]
    public void Rclone_IsAnS3ConfigBlockWithPathStyle()
    {
        var snippet = S3ClientSnippets.Rclone(Endpoint, Key, null);
        Assert.StartsWith("[objex]", snippet);
        Assert.Contains("type = s3", snippet);
        Assert.Contains("provider = Other", snippet);
        Assert.Contains("force_path_style = true", snippet);
    }

    [Fact]
    public void Env_ExportsTheFourAwsVariables_AndNoAddressingStyle()
    {
        var snippet = S3ClientSnippets.Env(Endpoint, Key, null);
        Assert.Contains("AWS_ACCESS_KEY_ID", snippet);
        Assert.Contains("AWS_SECRET_ACCESS_KEY", snippet);
        Assert.Contains("AWS_ENDPOINT_URL", snippet);
        Assert.Contains("AWS_DEFAULT_REGION", snippet);
        Assert.DoesNotContain("addressing", snippet, StringComparison.OrdinalIgnoreCase);
    }
}
