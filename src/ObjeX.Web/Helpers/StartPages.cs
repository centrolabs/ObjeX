namespace ObjeX.Web.Helpers;

/// <summary>The pages a user can choose to land on after login. Only these paths are ever followed from the cookie.</summary>
public static class StartPages
{
    public const string CookieName = "objex-start";
    public const string Default = "/";

    /// <summary>Roles null means every role. The icon is the one of the sidebar entry.</summary>
    public record Page(string Path, string Label, string Icon, string[]? Roles = null);

    /// <summary>The pages of the sidebar; the command palette lists them too.</summary>
    public static readonly IReadOnlyList<Page> All =
    [
        new("/", "Dashboard", "dashboard"),
        new("/buckets", "Buckets", "inventory_2"),
        new("/audit", "Audit Log", "history", ["Admin"]),
        new("/users", "Users", "group", ["Admin", "Manager"]),
        new("/jobs", "Jobs", "schedule", ["Admin"]),
        new("/settings", "Settings", "settings")
    ];

    public static IReadOnlyList<Page> For(Func<string, bool> isInRole) =>
        All.Where(p => p.Roles is null || p.Roles.Any(isInRole)).ToList();

    /// <summary>The chosen page when it is on the list and the user may open it, else the dashboard.</summary>
    public static string Resolve(string? cookie, Func<string, bool> isInRole) =>
        For(isInRole).FirstOrDefault(p => p.Path == cookie)?.Path ?? Default;
}
