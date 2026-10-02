namespace ObjeX.Web.Helpers;

/// <summary>Flags S3 credentials that nobody seems to need, so the Settings page can suggest deleting them.</summary>
public static class CredentialUsage
{
    /// <summary>A new credential has this long to be used before "Never used" counts as a warning.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromDays(7);
    public static readonly TimeSpan Stale = TimeSpan.FromDays(90);

    /// <summary>"Never used" or "Unused 90+ days", or null while the credential looks in use.</summary>
    public static string? Warning(DateTime createdUtc, DateTime? lastUsedUtc, DateTime nowUtc) => lastUsedUtc switch
    {
        null when nowUtc - DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc) >= Grace => "Never used",
        { } used when nowUtc - DateTime.SpecifyKind(used, DateTimeKind.Utc) >= Stale => "Unused 90+ days",
        _ => null
    };
}
