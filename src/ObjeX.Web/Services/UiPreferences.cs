using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.JSInterop;
using ObjeX.Web.Helpers;
using Radzen;

namespace ObjeX.Web.Services;

/// <summary>
/// The appearance choices of this browser: theme, sidebar, clock, relative times, start page. Each lives in its own
/// cookie; Routes loads them, so the first render already matches. A change writes the cookie through the browser and
/// raises Changed for every component that shows it.
/// </summary>
public sealed class UiPreferences(IJSRuntime js, ThemeService themes, BrowserTimeZone tz)
{
    public const string SidebarCookie = "objex-sidebar";
    public const string SidebarWidthCookie = "objex-sidebar-width";
    public const string ClockCookie = "objex-clock";
    public const string TimesCookie = "objex-times";

    public const int SidebarMinWidth = 200;
    public const int SidebarMaxWidth = 360;

    const string Collapsed = "collapsed";

    public bool IsDark { get; private set; }
    public bool SidebarCollapsed { get; private set; }
    /// <summary>Null keeps the width of the design tokens.</summary>
    public int? SidebarWidth { get; private set; }
    public bool Hour12 { get; private set; }
    public bool RelativeTimes { get; private set; } = true;
    /// <summary>The raw cookie; StartPages.Resolve checks it against the list and the user's roles.</summary>
    public string? StartPage { get; private set; }

    public event Action? Changed;

    static readonly string[] Names = [ThemeMode.CookieName, SidebarCookie, SidebarWidthCookie, ClockCookie, TimesCookie, StartPages.CookieName];

    /// <summary>The cookies Routes needs, read while prerendering; inside the circuit there is no request.</summary>
    public static Dictionary<string, string> CookiesFrom(IRequestCookieCollection? cookies) =>
        cookies is null ? [] : Names.Where(cookies.ContainsKey).ToDictionary(n => n, n => cookies[n] ?? string.Empty);

    public void Load(IReadOnlyDictionary<string, string>? cookies)
    {
        string? Get(string name) => cookies is not null && cookies.TryGetValue(name, out var value) ? value : null;

        IsDark = ThemeMode.IsDark(Get(ThemeMode.CookieName));
        SidebarCollapsed = Get(SidebarCookie) == Collapsed;
        SidebarWidth = int.TryParse(Get(SidebarWidthCookie), NumberStyles.None, CultureInfo.InvariantCulture, out var width)
            ? Math.Clamp(width, SidebarMinWidth, SidebarMaxWidth)
            : null;
        Hour12 = Get(ClockCookie) == "12";
        RelativeTimes = Get(TimesCookie) != "absolute";
        StartPage = Get(StartPages.CookieName);
        tz.Hour12 = Hour12;
    }

    public async Task SetDarkAsync(bool dark)
    {
        IsDark = dark;
        Changed?.Invoke();
        var theme = ThemeMode.CookieValue(dark);
        themes.SetTheme(theme);
        await WriteAsync(ThemeMode.CookieName, theme);
        await js.InvokeVoidAsync("ObjeX.setTheme", dark);
    }

    public async Task SetSidebarCollapsedAsync(bool collapsed)
    {
        SidebarCollapsed = collapsed;
        Changed?.Invoke();
        await WriteAsync(SidebarCookie, collapsed ? Collapsed : "expanded");
    }

    /// <summary>Null goes back to the width of the design tokens.</summary>
    public async Task SetSidebarWidthAsync(int? width)
    {
        SidebarWidth = width is { } w ? Math.Clamp(w, SidebarMinWidth, SidebarMaxWidth) : null;
        Changed?.Invoke();
        await WriteAsync(SidebarWidthCookie, SidebarWidth?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
    }

    public async Task SetHour12Async(bool hour12)
    {
        Hour12 = hour12;
        tz.Hour12 = hour12;
        Changed?.Invoke();
        await WriteAsync(ClockCookie, hour12 ? "12" : "24");
    }

    public async Task SetRelativeTimesAsync(bool relative)
    {
        RelativeTimes = relative;
        Changed?.Invoke();
        await WriteAsync(TimesCookie, relative ? "relative" : "absolute");
    }

    public async Task SetStartPageAsync(string path)
    {
        StartPage = path;
        Changed?.Invoke();
        await WriteAsync(StartPages.CookieName, path);
    }

    Task WriteAsync(string name, string value) => js.InvokeVoidAsync("ObjeX.setCookie", name, value).AsTask();
}
