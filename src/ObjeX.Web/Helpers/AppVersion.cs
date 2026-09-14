using System.Reflection;

namespace ObjeX.Web.Helpers;

public static class AppVersion
{
    /// <summary>The version from Directory.Build.props, without the "+{git sha}" suffix the SDK appends.</summary>
    public static string Version { get; }

    /// <summary>"ObjeX 1.2.2 (abc1234)", or "ObjeX 1.2.2" when the build embedded no sha.</summary>
    public static string Display { get; }

    static AppVersion()
    {
        var informational = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

        var plus = informational.IndexOf('+');
        Version = plus < 0 ? informational : informational[..plus];

        var sha = plus < 0 ? string.Empty : informational[(plus + 1)..];
        Display = sha.Length == 0
            ? $"ObjeX {Version}"
            : $"ObjeX {Version} ({sha[..Math.Min(7, sha.Length)]})";
    }
}
