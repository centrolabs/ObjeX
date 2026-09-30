using System.Reflection;

namespace ObjeX.Web.Helpers;

public static class AppVersion
{
    const string LocalVersion = "0.0.0";

    /// <summary>The release version passed by CD (0.0.0 in local builds), without the "+{git sha}" suffix the SDK appends.</summary>
    public static string Version { get; }

    /// <summary>"ObjeX 1.2.2 (abc1234)", or "ObjeX 1.2.2" when the build embedded no sha. A local build says "dev" instead of 0.0.0.</summary>
    public static string Display { get; }

    static AppVersion()
    {
        var informational = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? LocalVersion;

        var plus = informational.IndexOf('+');
        Version = plus < 0 ? informational : informational[..plus];

        var sha = plus < 0 ? string.Empty : informational[(plus + 1)..];
        Display = Format(Version, sha);
    }

    public static string Format(string version, string sha)
    {
        var shown = version == LocalVersion ? "dev" : version;
        return sha.Length == 0
            ? $"ObjeX {shown}"
            : $"ObjeX {shown} ({sha[..Math.Min(7, sha.Length)]})";
    }
}
