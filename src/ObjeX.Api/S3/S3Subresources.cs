namespace ObjeX.Api.S3;

/// <summary>
/// Query subresources ObjeX does not implement. A request carrying one must get 501 instead of falling
/// through to the plain bucket or object handler, which would create, overwrite or delete.
/// </summary>
public static class S3Subresources
{
    private static readonly string[] Bucket =
    [
        "accelerate", "acl", "analytics", "cors", "encryption", "intelligent-tiering", "inventory", "lifecycle",
        "logging", "metrics", "notification", "object-lock", "ownershipControls", "policy", "policyStatus",
        "publicAccessBlock", "replication", "requestPayment", "tagging", "versioning", "website",
    ];

    private static readonly string[] Object =
        ["acl", "attributes", "legal-hold", "restore", "retention", "select", "tagging", "torrent"];

    public static bool IsUnsupportedOnBucket(HttpRequest request) => Bucket.Any(request.Query.ContainsKey);

    public static bool IsUnsupportedOnObject(HttpRequest request) => Object.Any(request.Query.ContainsKey);

    public static IResult NotImplemented() =>
        S3Xml.Error(S3Errors.NotImplemented, "This operation is not yet supported.", StatusCodes.Status501NotImplemented);
}
