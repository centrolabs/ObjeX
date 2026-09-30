using System.Text.RegularExpressions;

namespace ObjeX.Tests.Unit;

/// <summary>
/// Guards the design system by reading the UI sources as text: colours live in tokens.css only, inline styles
/// in Components/Ui only, and Radzen serves grids, charts, popups, dropdowns and numeric inputs, not layout.
/// </summary>
public class UiRulesTests
{
    private static readonly string Src = FindSrc();
    private static readonly string UiFolder = Path.Combine(Src, "ObjeX.Web", "Components", "Ui") + Path.DirectorySeparatorChar;
    private static readonly string TokenFile = Path.Combine(Src, "ObjeX.Api", "wwwroot", "tokens.css");

    private static readonly Regex InlineStyle = new(@"\bstyle\s*=\s*""|<style\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Not preceded by & or a word character, so &#160; and url fragments stay legal; href="#" has no digits at all.
    private static readonly Regex ColourValue = new(
        @"(?<![&\w])#(?:[0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{3,4})\b|\b(?:rgba?|hsla?)\(",
        RegexOptions.Compiled);

    private static readonly Regex LayoutComponent = new(
        @"<Radzen(?:Stack|Text|Button|Card|Layout|Sidebar|PanelMenu|PanelMenuItem|Badge|FormField)\b", RegexOptions.Compiled);

    private static readonly Regex RadzenComponent = new(@"<(Radzen\w+)", RegexOptions.Compiled);

    private static readonly Regex AllowedRadzen = new(
        @"^Radzen(?:DataGrid|DataGridColumn|Chart|\w+Series|ChartTooltipOptions|Legend|CategoryAxis|ValueAxis|AxisTitle|GridLines|SeriesDataLabels|Components|Dialog|Notification|ContextMenu|Tooltip|DropDown|Numeric|Theme)$",
        RegexOptions.Compiled);

    [Fact]
    public void InlineStyles_OnlyInsideTheUiLibrary()
    {
        var hits = Scan(Razor().Where(f => !InUi(f)), InlineStyle);
        Assert.True(hits.Count == 0, Report("style attribute or <style> block outside Components/Ui", hits));
    }

    [Fact]
    public void ColourValues_OnlyInTheTokenFile()
    {
        var files = Razor().Concat(Files("*.css")).Concat(Files("*.razor.js")).Where(f => f != TokenFile);
        var hits = Scan(files, ColourValue);
        Assert.True(hits.Count == 0, Report("colour value outside tokens.css", hits));
    }

    [Fact]
    public void RadzenLayoutComponents_AreNotUsed()
    {
        var hits = Scan(Razor(), LayoutComponent);
        Assert.True(hits.Count == 0, Report("Radzen layout component", hits));
    }

    [Fact]
    public void RadzenComponents_AreOnTheAllowList()
    {
        var hits = Razor()
            .SelectMany(f => File.ReadLines(f).SelectMany((line, i) => RadzenComponent.Matches(line)
                .Where(m => !AllowedRadzen.IsMatch(m.Groups[1].Value))
                .Select(m => $"{Relative(f)}:{i + 1}  {m.Groups[1].Value}")))
            .ToList();
        Assert.True(hits.Count == 0, Report("Radzen component that the Ui library replaces", hits));
    }

    [Fact]
    public void TheScan_FindsTheSources()
    {
        Assert.True(File.Exists(TokenFile), TokenFile);
        Assert.Contains(Razor(), InUi);
        Assert.Contains(Razor(), f => !InUi(f));
    }

    private static IEnumerable<string> Razor() => Files("*.razor");

    private static IEnumerable<string> Files(string pattern) =>
        new[] { "ObjeX.Web", "ObjeX.Api" }
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(Src, project), pattern, SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    private static bool InUi(string file) => file.StartsWith(UiFolder, StringComparison.Ordinal);

    private static List<string> Scan(IEnumerable<string> files, Regex rule) =>
        files.SelectMany(f => File.ReadLines(f)
                .Select((line, i) => (line, i))
                .Where(x => rule.IsMatch(x.line))
                .Select(x => $"{Relative(f)}:{x.i + 1}  {x.line.Trim()}"))
            .ToList();

    private static string Relative(string file) => Path.GetRelativePath(Src, file);

    private static string Report(string what, List<string> hits) =>
        $"{hits.Count} x {what}:{Environment.NewLine}{string.Join(Environment.NewLine, hits.Take(60))}";

    private static string FindSrc()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "ObjeX.Web")) && Directory.Exists(Path.Combine(dir.FullName, "ObjeX.Api")))
                return dir.FullName;
        throw new DirectoryNotFoundException("src folder with ObjeX.Web and ObjeX.Api not found above " + AppContext.BaseDirectory);
    }
}
