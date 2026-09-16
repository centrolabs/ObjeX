namespace ObjeX.Core.Utilities;

/// <summary>What ObjeX advertises to S3 clients: one fixed region and path-style only; SigV4 does not validate the region.</summary>
public static class S3Conventions
{
    public const string Region = "us-east-1";
    public const string AddressingStyle = "path-style";
}
