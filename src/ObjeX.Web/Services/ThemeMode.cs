namespace ObjeX.Web.Services;

/// <summary>
/// Maps the <c>objex-theme</c> cookie to the Radzen base theme and the token mode class.
/// Values written before the design system (<c>material</c>, <c>material-dark</c>) keep their mode.
/// </summary>
public static class ThemeMode
{
    public const string CookieName = "objex-theme";
    public const string Light = "standard";
    public const string Dark = "standard-dark";

    public static bool IsDark(string? cookie) =>
        cookie is not null && cookie.Trim().EndsWith("dark", StringComparison.OrdinalIgnoreCase);

    /// <summary>The Radzen base theme. An unknown or missing value is light.</summary>
    public static string Radzen(string? cookie) => IsDark(cookie) ? Dark : Light;

    /// <summary>The class on <c>&lt;html&gt;</c> that selects the semantic token set.</summary>
    public static string CssClass(string? cookie) => IsDark(cookie) ? "ox-dark" : "ox-light";

    public static string CookieValue(bool dark) => dark ? Dark : Light;
}
