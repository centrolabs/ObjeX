namespace ObjeX.Web.Helpers;

/// <summary>Where a login returns to: the login endpoint and the login page for a user who is already signed in use the same rule.</summary>
public static class LoginTarget
{
    /// <summary>
    /// The page to return to, or null for none. RedirectToLogin sends the absolute URL of the page, so a URL on this host
    /// (<paramref name="host"/>, with its port) becomes its path and query; any other host is ignored. The root and the
    /// not-found page, where an anonymous request for an Admin page ends up, count as no particular page. Control characters
    /// are refused, because browsers drop tab and newline from a Location: "/\t/evil.example" would become "//evil.example".
    /// </summary>
    public static string? Local(string? returnUrl, string host)
    {
        if (string.IsNullOrEmpty(returnUrl) || returnUrl.Any(char.IsControl)) return null;
        if (Uri.TryCreate(returnUrl, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
        {
            if (!string.Equals(absolute.Authority, host, StringComparison.OrdinalIgnoreCase)) return null;
            returnUrl = absolute.PathAndQuery;
        }

        var local = returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\");
        return local && returnUrl.Split('?', '#')[0] is not ("/" or "/not-found") ? returnUrl : null;
    }
}
