using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Paperbunkr.App.Tests.Theming;

/// <summary>
/// Stops new hardcoded colours creeping into XAML (docs/superpowers/specs/2026-10-12-theme-system-v2-gap-analysis-and-plan.md, phase 0;
/// design doc section 6.5). A colour literal in a view does not follow the theme, which is a real defect for every theme but the one the
/// author happened to be looking at. The baseline file records how many literals each view had when this check was introduced - some are
/// intentional (the always-dark Detail hero, overlays drawn on page art) - and the test fails when a file gains one or a new file gets any.
/// Fix by using a <c>Pb*</c> token; if a literal really is intentional, regenerate with <c>PB_UPDATE_GOLDENS=1</c> and review the diff.
/// </summary>
public partial class ThemeHardcodedColorLintTests
{
    [GeneratedRegex("#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6})(?![0-9A-Fa-f])")]
    private static partial Regex HexColor();

    [Fact]
    public void NoViewGainsAHardcodedColour()
    {
        string appRoot = AppRoot();
        var actual = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(appRoot, "*.axaml", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(appRoot, file).Replace('\\', '/');
            if (relative.StartsWith("bin/", StringComparison.Ordinal) || relative.StartsWith("obj/", StringComparison.Ordinal))
            {
                continue;
            }

            int count = HexColor().Matches(StripComments(File.ReadAllText(file))).Count;
            if (count > 0)
            {
                actual[relative] = count;
            }
        }

        string baselinePath = Path.Combine(TestFolder(), "Goldens", "hardcoded-colors.json");
        if (Environment.GetEnvironmentVariable("PB_UPDATE_GOLDENS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
            File.WriteAllText(baselinePath, JsonSerializer.Serialize(actual, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        Assert.True(File.Exists(baselinePath), $"No baseline. Run once with PB_UPDATE_GOLDENS=1 to create {baselinePath}.");
        var baseline = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(baselinePath))!;

        var regressions = actual
            .Where(pair => pair.Value > baseline.GetValueOrDefault(pair.Key))
            .Select(pair => $"{pair.Key}: {pair.Value} colour literals (baseline {baseline.GetValueOrDefault(pair.Key)})")
            .ToList();

        Assert.True(regressions.Count == 0,
            "These views gained hardcoded colours. Use a Pb* token (DynamicResource) so the colour follows the theme:" + Environment.NewLine + string.Join(Environment.NewLine, regressions));
    }

    /// <summary>A hex value quoted in a comment is documentation, not a colour in use.</summary>
    private static string StripComments(string xaml) => Regex.Replace(xaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    private static string TestFolder([CallerFilePath] string caller = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(caller)!, ".."));

    private static string AppRoot() => Path.GetFullPath(Path.Combine(TestFolder(), "..", "Paperbunkr.App"));
}
