namespace ObjeX.Web.Helpers;

/// <summary>The pages a user can choose to land on after login. Only these paths are ever followed from the cookie.</summary>
public static class StartPages
{
    public const string CookieName = "objex-start";
    public const string Default = "/";

    /// <summary>Roles null means every role.</summary>
    public record Page(string Path, string Label, string[]? Roles = null);

    public static readonly IReadOnlyList<Page> All =
    [
        new("/", "Dashboard"),
        new("/buckets", "Buckets"),
        new("/audit", "Audit Log", ["Admin"]),
        new("/users", "Users", ["Admin", "Manager"]),
        new("/jobs", "Jobs", ["Admin"]),
        new("/settings", "Settings")
    ];

    public static IReadOnlyList<Page> For(Func<string, bool> isInRole) =>
        All.Where(p => p.Roles is null || p.Roles.Any(isInRole)).ToList();

    /// <summary>The chosen page when it is on the list and the user may open it, else the dashboard.</summary>
    public static string Resolve(string? cookie, Func<string, bool> isInRole) =>
        For(isInRole).FirstOrDefault(p => p.Path == cookie)?.Path ?? Default;
}
