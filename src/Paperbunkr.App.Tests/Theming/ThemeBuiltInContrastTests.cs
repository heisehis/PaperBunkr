using Avalonia.Media;
using Paperbunkr.App.Theming;
using Paperbunkr.App.Theming.Runtime;

namespace Paperbunkr.App.Tests.Theming;

/// <summary>
/// The contrast floor every built-in theme must meet (docs/superpowers/specs/2026-11-09-theme-gallery-and-roster-design.md Q19): body text at least 4.5:1 and the
/// accent at least 3:1 against the canvas. A texture canvas is checked through its plain colour, which is the texture's own base colour.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ThemeBuiltInContrastTests
{
    // Family keys only: the catalog cannot read built-in assets during test discovery, before Avalonia is up, so variants are expanded in the test.
    public static TheoryData<string> BuiltInFamilies()
    {
        var data = new TheoryData<string>();
        foreach (string key in ThemeCatalog.BuiltInKeys)
        {
            data.Add(key);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(BuiltInFamilies))]
    public void EveryBuiltInVariant_MeetsTheContrastFloor(string familyKey)
    {
        var document = new ThemeCatalog().Load(familyKey) ?? throw new Xunit.Sdk.XunitException($"{familyKey} did not load");
        var failures = new List<string>();
        foreach (var variant in document.Variants)
        {
            var theme = ThemeResolver.Resolve(document, variant.Id);
            string key = document.ThemeKeyFor(variant);
            Color canvas = theme.Color("surface.canvas") ?? throw new Xunit.Sdk.XunitException($"{key}: no canvas colour");
            Color text = theme.Color("text.primary") ?? throw new Xunit.Sdk.XunitException($"{key}: no text colour");
            Color accent = theme.Color("action.primary") ?? throw new Xunit.Sdk.XunitException($"{key}: no accent colour");

            double textRatio = ThemeColorMath.ContrastRatio(text, canvas);
            double accentRatio = ThemeColorMath.ContrastRatio(accent, canvas);
            if (textRatio < 4.5)
            {
                failures.Add($"{key}: text {textRatio:0.00}:1 on its canvas, below 4.5:1");
            }

            if (accentRatio < 3.0)
            {
                failures.Add($"{key}: accent {accentRatio:0.00}:1 on its canvas, below 3:1");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
