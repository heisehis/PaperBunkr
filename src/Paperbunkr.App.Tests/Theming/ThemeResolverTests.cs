using System.Runtime.CompilerServices;
using Avalonia.Media;
using Paperbunkr.App.Theming;

namespace Paperbunkr.App.Tests.Theming;

/// <summary>
/// The pure theme resolver (docs/superpowers/specs/2026-10-12-theme-system-v2-gap-analysis-and-plan.md, phases 1 and 2). No application, no
/// dispatcher, no Avalonia test collection: only the <see cref="Color"/> value type is used.
/// </summary>
public class ThemeResolverTests
{
    private const string FamilyJson = """
        {
          "schemaVersion": 2,
          "id": "org.test.paper",
          "name": "Paper",
          "base": {
            "palette": { "canvas": "#F7F3EC", "ink": "#2A2620", "accent": "#985A27" },
            "semantic": {
              "surface.canvas": { "ref": "palette.canvas" },
              "text.primary": { "ref": "palette.ink" },
              "action.primary": { "ref": "semantic.action.brand" },
              "action.brand": { "ref": "palette.accent" }
            },
            "metrics": { "radius": 9 },
            "components": {
              "button.primary": { "background": { "ref": "action.primary" }, "radius": 7, "states": { "hover": { "background": "#AA6A30" } } }
            }
          },
          "variants": {
            "light": { "mode": "Light" },
            "dark": { "mode": "Dark", "palette": { "canvas": "#12100D", "ink": "#EDE0C8" } },
            "oled": { "mode": "Dark", "palette": { "canvas": "#000000", "ink": "#EDE0C8" }, "semantic": { "surface.raised": "#0A0A0A" } }
          }
        }
        """;

    private static ThemeDocument Parse(string json, out List<ThemeDiagnostic> diagnostics, string key = "paper", Func<string, string?>? files = null)
    {
        diagnostics = new List<ThemeDiagnostic>();
        var document = ThemeDocumentParser.Parse(json, key, diagnostics, files);
        Assert.NotNull(document);
        return document!;
    }

    private static ThemeDocument Family() => Parse(FamilyJson, out _);

    private static Color C(string hex) => Color.Parse(hex);

    private static string SkinsFolder([CallerFilePath] string caller = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(caller)!, "..", "..", "Paperbunkr.App", "Assets", "Skins"));

    // ===================== Schema and families =====================

    [Fact]
    public void Parse_ReadsFamilyVariantsInOrder_WithModesAndKinds()
    {
        var document = Family();

        Assert.Equal("org.test.paper", document.Id);
        Assert.Equal("paper", document.Key);
        Assert.False(document.IsLegacy);
        Assert.Equal(new[] { "light", "dark", "oled" }, document.Variants.Select(v => v.Id));
        Assert.Equal(new[] { "Light", "Dark", "Dark" }, document.Variants.Select(v => v.Mode));
        Assert.Equal(new[] { ThemeVariantKind.Light, ThemeVariantKind.Dark, ThemeVariantKind.Oled }, document.Variants.Select(v => v.Kind));
    }

    [Fact]
    public void ThemeKeys_ComposeFamilyAndVariant_AndSplitBack()
    {
        var document = Family();

        Assert.Equal("paper:dark", document.ThemeKeyFor(document.FindVariant("dark")!));
        Assert.Equal(("paper", "dark"), ThemeDocument.SplitKey("paper:dark"));
        Assert.Equal(("default", null), ThemeDocument.SplitKey("default"));
    }

    [Fact]
    public void Parse_AFamilyWithNoVariantsBlock_GetsOneDefaultVariant_AndKeepsABareKey()
    {
        var document = Parse("""{ "schemaVersion": 2, "name": "Solo", "mode": "Light", "base": { "palette": { "canvas": "#FFFFFF" } } }""", out _, "solo");

        Assert.Single(document.Variants);
        Assert.Equal(ThemeDocument.DefaultVariantId, document.DefaultVariant.Id);
        Assert.True(document.DefaultVariant.IsLight);
        Assert.Equal("solo", document.ThemeKeyFor(document.DefaultVariant));
    }

    [Fact]
    public void Parse_NewerSchema_IsRefusedWithADiagnostic()
    {
        var diagnostics = new List<ThemeDiagnostic>();

        var document = ThemeDocumentParser.Parse("""{ "schemaVersion": 3, "name": "Future" }""", "future", diagnostics);

        Assert.Null(document);
        Assert.Contains(diagnostics, d => d.Code == ThemeDiagnosticCodes.UnsupportedSchema && d.Severity == ThemeDiagnosticSeverity.Error);
    }

    [Fact]
    public void Parse_InvalidJson_IsRefusedWithADiagnostic_NotAnException()
    {
        var diagnostics = new List<ThemeDiagnostic>();

        Assert.Null(ThemeDocumentParser.Parse("{ not json", "broken", diagnostics));
        Assert.Contains(diagnostics, d => d.Code == ThemeDiagnosticCodes.InvalidJson);
    }

    [Fact]
    public void Parse_TooManyVariants_KeepsTheFirstEightAndReports()
    {
        string variants = string.Join(",", Enumerable.Range(0, 12).Select(i => $"\"v{i}\": {{ \"mode\": \"Dark\" }}"));
        var diagnostics = new List<ThemeDiagnostic>();

        var document = ThemeDocumentParser.Parse($"{{ \"schemaVersion\": 2, \"name\": \"Many\", \"variants\": {{ {variants} }} }}", "many", diagnostics);

        Assert.Equal(ThemeDocumentParser.MaxVariants, document!.Variants.Count);
        Assert.Contains(diagnostics, d => d.Code == ThemeDiagnosticCodes.LimitExceeded);
    }

    // ===================== Legacy (v1) compatibility =====================

    public static TheoryData<string> BuiltInKeys => new()
    {
        "default", "windows_11", "cool_technical", "vibrant_pop", "vintage_paperback",
        "daylight", "overcast", "maximum_contrast", "colorblind_safe", "matrix",
    };

    [Theory]
    [MemberData(nameof(BuiltInKeys))]
    public void LegacyThemeJson_BecomesASingleVariantFamily_WithEveryColourAuthored(string key)
    {
        string json = File.ReadAllText(Path.Combine(SkinsFolder(), key, "theme.json"));

        var document = Parse(json, out var diagnostics, key);
        var resolved = ThemeResolver.Resolve(document, null);

        Assert.True(document.IsLegacy);
        Assert.Equal("legacy." + key, document.Id);
        Assert.Single(document.Variants);
        Assert.Equal(key, resolved.ThemeKey);
        Assert.DoesNotContain(diagnostics.Concat(resolved.Diagnostics), d => d.Severity == ThemeDiagnosticSeverity.Error);

        // Every legacy colour is authored, never derived - which is what makes the bridge reproduce the theme exactly.
        foreach (string role in new[] { "surface.canvas", "surface.navigation", "text.primary", "action.primary", "text.accent", "glow.color", "surface.overlay" })
        {
            Assert.Equal(ThemeValueSource.FamilyAuthored, resolved.Token(role)!.Source);
        }
    }

    [Fact]
    public void LegacyThemeJson_MissingNewerFields_FallsBackToTheOriginalDefaults_NotToDerivedValues()
    {
        var document = Parse("""{"name":"Old","colors":{"bg":"#111111","text":"#EEEEEE","accent":"#4488FF"},"radius":8}""", out _, "old");
        var resolved = ThemeResolver.Resolve(document, null);

        Assert.Equal(C("#111111"), resolved.Color("surface.canvas"));
        Assert.Equal(C("#000000"), resolved.Color("surface.recessed"));        // SkinColors.Surface0's default
        Assert.Equal(C("#66E0995A"), resolved.Color("glow.color"));            // SkinColors.Glow's default
        Assert.Equal(8, resolved.Number("metrics.radius"));
        Assert.Equal(5, resolved.Number("metrics.radiusSm"));
    }

    [Fact]
    public void LegacyRoundTrip_FromResolved_KeepsNameModeAndColours()
    {
        string json = File.ReadAllText(Path.Combine(SkinsFolder(), "matrix", "theme.json"));
        var resolved = ThemeResolver.Resolve(Parse(json, out _, "matrix"), null);

        var legacy = LegacyThemeAdapter.FromResolved(resolved);

        Assert.Equal("Matrix", legacy.Name);
        Assert.Equal("Dark", legacy.Mode);
        Assert.Equal("matrix", legacy.Category);
        Assert.Equal(C("#33FF66"), C(legacy.Colors.Accent));
        Assert.NotNull(legacy.MatrixGlyphs);
        Assert.Contains("Cascadia Code", legacy.DefaultFontFamily);
        Assert.Null(legacy.Colors.EraGolden);                                   // not authored, so not reported as authored
    }

    // ===================== Inheritance, references, derivation =====================

    [Fact]
    public void Variant_InheritsTheBase_AndItsOwnValuesWin()
    {
        var document = Family();

        var light = ThemeResolver.Resolve(document, "light");
        var dark = ThemeResolver.Resolve(document, "dark");

        Assert.Equal(C("#F7F3EC"), light.Color("surface.canvas"));
        Assert.Equal(C("#12100D"), dark.Color("surface.canvas"));
        Assert.Equal(C("#985A27"), dark.Color("action.primary"));               // inherited from the base
        Assert.Equal(9, dark.Number("metrics.radius"));
        Assert.True(light.IsLight);
        Assert.False(dark.IsLight);
    }

    [Fact]
    public void References_ChainThroughPaletteAndSemantic_AndAcceptTheSemanticPrefix()
    {
        var resolved = ThemeResolver.Resolve(Family(), "light");

        Assert.Equal(C("#985A27"), resolved.Color("action.primary"));
        Assert.Equal(C("#985A27"), resolved.Color("semantic.action.primary"));
        Assert.Equal("ref action.brand", resolved.Token("action.primary")!.Note);
    }

    [Fact]
    public void Provenance_SaysWhetherAValueWasAuthoredOrDerived()
    {
        var dark = ThemeResolver.Resolve(Family(), "dark");

        Assert.Equal(ThemeValueSource.FamilyAuthored, dark.Token("surface.canvas")!.Source);     // the role is set in the base (as a ref)
        Assert.Equal(ThemeValueSource.VariantAuthored, dark.Token("palette.canvas")!.Source);
        Assert.Equal(ThemeValueSource.Derived, dark.Token("text.secondary")!.Source);
        Assert.True(dark.Token("border.default")!.IsDerived);
    }

    [Fact]
    public void AMinimalTheme_ResolvesEveryColourRole_Deterministically()
    {
        var document = Parse("""{ "schemaVersion": 2, "name": "Tiny", "mode": "Dark", "base": { "palette": { "canvas": "#101418", "ink": "#E6EAF0", "accent": "#4DA3FF" } } }""", out _, "tiny");

        var first = ThemeResolver.Resolve(document, null);
        var second = ThemeResolver.Resolve(document, null);

        foreach (var role in ThemeVocabulary.Roles.Where(r => r.Kind == ThemeValueKind.Color))
        {
            Assert.True(first.Color(role.Path) is not null, $"{role.Path} did not resolve");
            Assert.Equal(first.Color(role.Path), second.Color(role.Path));
        }

        Assert.Equal(C("#101418"), first.Color("surface.canvas"));
        Assert.True(ThemeColorMath.ContrastRatio(first.Color("text.accent")!.Value, first.Color("surface.canvas")!.Value) >= 4.5);
        Assert.DoesNotContain(first.Diagnostics, d => d.Severity == ThemeDiagnosticSeverity.Error);
    }

    [Fact]
    public void AnExplicitValue_AlwaysBeatsADerivedOne()
    {
        var document = Parse("""{ "schemaVersion": 2, "name": "T", "base": { "palette": { "canvas": "#101418", "ink": "#E6EAF0", "accent": "#4DA3FF" }, "semantic": { "text.secondary": "#123456" } } }""", out _, "t");

        var resolved = ThemeResolver.Resolve(document, null);

        Assert.Equal(C("#123456"), resolved.Color("text.secondary"));
        Assert.Equal(ThemeValueSource.FamilyAuthored, resolved.Token("text.secondary")!.Source);
    }

    [Fact]
    public void AReferenceCycle_IsReported_AndTheRolesFallBackToDerivedValues()
    {
        var document = Parse("""{ "schemaVersion": 2, "name": "Loop", "base": { "semantic": { "surface.canvas": { "ref": "text.primary" }, "text.primary": { "ref": "surface.canvas" } } } }""", out _, "loop");

        var resolved = ThemeResolver.Resolve(document, null);

        Assert.Contains(resolved.Diagnostics, d => d.Code == ThemeDiagnosticCodes.ReferenceCycle && d.Severity == ThemeDiagnosticSeverity.Error);
        Assert.NotNull(resolved.Color("surface.canvas"));                       // still usable: derived from the mode default
        Assert.NotNull(resolved.Color("text.primary"));
    }

    [Fact]
    public void AnUnknownReference_IsReported_AndFallsBack()
    {
        var document = Parse("""{ "schemaVersion": 2, "name": "U", "base": { "semantic": { "action.primary": { "ref": "palette.nope" } } } }""", out _, "u");

        var resolved = ThemeResolver.Resolve(document, null);

        Assert.Contains(resolved.Diagnostics, d => d.Code == ThemeDiagnosticCodes.UnknownReference && d.PropertyPath == "action.primary");
        Assert.Equal(ThemeValueSource.Derived, resolved.Token("action.primary")!.Source);
    }

    [Fact]
    public void AWrongType_IsReported_AndFallsBack()
    {
        var document = Parse("""{ "schemaVersion": 2, "name": "W", "base": { "semantic": { "surface.canvas": 12 }, "metrics": { "radius": "big" } } }""", out _, "w");

        var resolved = ThemeResolver.Resolve(document, null);

        Assert.Contains(resolved.Diagnostics, d => d.Code == ThemeDiagnosticCodes.TypeMismatch && d.PropertyPath == "surface.canvas");
        Assert.Contains(resolved.Diagnostics, d => d.Code == ThemeDiagnosticCodes.TypeMismatch && d.PropertyPath == "metrics.radius");
        Assert.NotNull(resolved.Color("surface.canvas"));
        Assert.Equal(7, resolved.Number("metrics.radius"));
    }

    [Fact]
    public void AMissingVariant_FallsBackToTheFirst_WithAWarning()
    {
        var resolved = ThemeResolver.Resolve(Family(), "sepia");

        Assert.Equal("light", resolved.VariantId);
        Assert.Contains(resolved.Diagnostics, d => d.Code == ThemeDiagnosticCodes.MissingVariant && d.FallbackUsed);
    }

    [Fact]
    public void Components_ResolveReferencesAndStates_AndAStateFallsBackToNormal()
    {
        var resolved = ThemeResolver.Resolve(Family(), "light");

        Assert.Equal(C("#985A27"), resolved.Component("button.primary", "normal", "background")!.Value.Color);
        Assert.Equal(C("#AA6A30"), resolved.Component("button.primary", "hover", "background")!.Value.Color);
        Assert.Equal(7, resolved.Component("button.primary", "pressed", "radius")!.Value.Number);    // no pressed radius: the normal one
        Assert.Null(resolved.Component("button.primary", "normal", "nope"));
    }

    // ===================== User overrides (precedence, section 6.3) =====================

    [Fact]
    public void Overrides_GlobalThenFamilyThenVariant_EachBeatsTheOneBelow()
    {
        var document = Family();
        var overrides = new UserThemeOverrides();
        overrides.Global.Values["action.primary"] = "#111111";

        Assert.Equal(C("#111111"), ThemeResolver.Resolve(document, "dark", overrides).Color("action.primary"));
        Assert.Equal(ThemeValueSource.UserGlobal, ThemeResolver.Resolve(document, "dark", overrides).Token("action.primary")!.Source);

        overrides.GetOrAdd(ThemeOverrideScope.Family, document.Id, "dark").Values["action.primary"] = "#222222";
        Assert.Equal(C("#222222"), ThemeResolver.Resolve(document, "dark", overrides).Color("action.primary"));
        Assert.Equal(ThemeValueSource.UserFamily, ThemeResolver.Resolve(document, "light", overrides).Token("action.primary")!.Source);

        overrides.GetOrAdd(ThemeOverrideScope.Variant, document.Id, "dark").Values["action.primary"] = "#333333";
        Assert.Equal(C("#333333"), ThemeResolver.Resolve(document, "dark", overrides).Color("action.primary"));
        Assert.Equal(C("#222222"), ThemeResolver.Resolve(document, "light", overrides).Color("action.primary"));   // the variant override is the dark variant's only
    }

    [Fact]
    public void Overrides_ForAnotherFamily_DoNotApply()
    {
        var overrides = new UserThemeOverrides();
        overrides.GetOrAdd(ThemeOverrideScope.Family, "org.someone.else", "dark").Values["action.primary"] = "#222222";

        Assert.Equal(C("#985A27"), ThemeResolver.Resolve(Family(), "dark", overrides).Color("action.primary"));
    }

    [Fact]
    public void Overrides_OfAComponentProperty_AreTaggedAsComponentSpecific_AndBeatTheTheme()
    {
        var document = Family();
        var overrides = new UserThemeOverrides();
        overrides.Global.Values[ThemeTokenPaths.Component("button.primary", "normal", "background")] = "#00FF00";

        var token = ThemeResolver.Resolve(document, "light", overrides).Component("button.primary", "normal", "background")!;

        Assert.Equal(C("#00FF00"), token.Value.Color);
        Assert.Equal(ThemeValueSource.UserComponent, token.Source);
    }

    [Fact]
    public void AnOverriddenRole_FlowsIntoWhateverIsDerivedFromIt()
    {
        var document = Parse("""{ "schemaVersion": 2, "name": "T", "mode": "Dark", "base": { "palette": { "canvas": "#101418", "ink": "#E6EAF0", "accent": "#4DA3FF" } } }""", out _, "t");
        var overrides = new UserThemeOverrides();
        overrides.Global.Values["surface.canvas"] = "#202020";

        var resolved = ThemeResolver.Resolve(document, null, overrides);

        Assert.Equal(C("#202020"), resolved.Color("surface.default"));          // derived from the canvas, so it follows
        Assert.Equal(ThemeValueSource.Derived, resolved.Token("surface.default")!.Source);
    }

    [Fact]
    public void AnInvalidOverrideValue_IsIgnored_AndTheThemeValueStands()
    {
        var overrides = new UserThemeOverrides();
        overrides.Global.Values["action.primary"] = "not-a-colour";

        var resolved = ThemeResolver.Resolve(Family(), "light", overrides);

        Assert.Equal(C("#985A27"), resolved.Color("action.primary"));
        Assert.Contains(resolved.Diagnostics, d => d.Code == ThemeDiagnosticCodes.TypeMismatch);
    }

    // ===================== The legacy accent override and True Black =====================

    [Fact]
    public void AccentOverride_ReplacesTheAccent_AndReDerivesItsDependents_EvenWhenTheThemeAuthoredThem()
    {
        string json = File.ReadAllText(Path.Combine(SkinsFolder(), "default", "theme.json"));
        var document = Parse(json, out _, "default");
        var environment = new ThemeEnvironment { AccentOverrideHex = "#3366CC" };

        var resolved = ThemeResolver.Resolve(document, null, null, environment);

        var accent = C("#3366CC");
        var expectedText = ThemeColorMath.AdjustForContrast(accent, C("#0A0B0D"), 4.5);
        Assert.Equal(accent, resolved.Color("action.primary"));
        Assert.Equal(expectedText, resolved.Color("text.accent"));
        Assert.Equal(ThemeColorMath.WithAlpha(accent, 0x29), resolved.Color("action.soft"));
        Assert.Equal(ThemeColorMath.WithAlpha(expectedText, 0x66), resolved.Color("glow.color"));
        Assert.Equal(accent, resolved.Color("era.modern"));
        Assert.Equal(ThemeValueSource.UserGlobal, resolved.Token("action.primary")!.Source);
    }

    [Fact]
    public void AFamilyScopedAccent_BeatsTheGlobalAccentOverride()
    {
        var document = Family();
        var overrides = new UserThemeOverrides();
        overrides.GetOrAdd(ThemeOverrideScope.Family, document.Id, "dark").Values["action.primary"] = "#FF0000";

        var resolved = ThemeResolver.Resolve(document, "dark", overrides, new ThemeEnvironment { AccentOverrideHex = "#3366CC" });

        Assert.Equal(C("#FF0000"), resolved.Color("action.primary"));
    }

    [Fact]
    public void TrueBlack_BlackensTheSixSurfaces_OfADarkVariantOnly()
    {
        var document = Family();
        var environment = new ThemeEnvironment { TrueBlack = true };

        var dark = ThemeResolver.Resolve(document, "dark", null, environment);
        var light = ThemeResolver.Resolve(document, "light", null, environment);

        foreach (string surface in ThemeVocabulary.TrueBlackSurfaces)
        {
            Assert.Equal(C("#000000"), dark.Color(surface));
            Assert.Equal(ThemeValueSource.TrueBlack, dark.Token(surface)!.Source);
        }

        Assert.True(dark.TrueBlackApplied);
        Assert.False(light.TrueBlackApplied);
        Assert.Equal(C("#F7F3EC"), light.Color("surface.canvas"));
    }

    [Fact]
    public void TrueBlack_LeavesADesignedOledVariantAlone()
    {
        var oled = ThemeResolver.Resolve(Family(), "oled", null, new ThemeEnvironment { TrueBlack = true });

        Assert.False(oled.TrueBlackApplied);
        Assert.Equal(C("#0A0A0A"), oled.Color("surface.raised"));               // the author's own surface hierarchy survives
    }

    // ===================== Accessibility (report-only by default) =====================

    [Fact]
    public void LowContrast_IsReported_AndTheAuthoredColourIsNotChanged()
    {
        string json = File.ReadAllText(Path.Combine(SkinsFolder(), "windows_11", "theme.json"));

        var resolved = ThemeResolver.Resolve(Parse(json, out _, "windows_11"), null);

        Assert.Equal(C("#0078D4"), resolved.Color("action.primary"));           // unchanged, by decision
        var warning = Assert.Single(resolved.Warnings, w => w.Code == ThemeDiagnosticCodes.LowContrast && w.PropertyPath == "action.primary");
        Assert.Contains("4.08:1", warning.Message);
    }

    [Fact]
    public void BoostContrast_RaisesTextRoles_AndOnlyWhenTheUserAsksForIt()
    {
        var document = Parse("""{ "schemaVersion": 2, "name": "Faint", "mode": "Dark", "base": { "palette": { "canvas": "#101010", "ink": "#555555", "accent": "#4DA3FF" } } }""", out _, "faint");

        var asAuthored = ThemeResolver.Resolve(document, null);
        var boosted = ThemeResolver.Resolve(document, null, new UserThemeOverrides { BoostContrast = true });

        Assert.Equal(C("#555555"), asAuthored.Color("text.primary"));
        Assert.Contains(asAuthored.Warnings, w => w.PropertyPath == "text.primary");

        var canvas = C("#101010");
        Assert.True(ThemeColorMath.ContrastRatio(boosted.Color("text.primary")!.Value, canvas) >= 4.5);
        Assert.Equal(ThemeValueSource.Accessibility, boosted.Token("text.primary")!.Source);
        Assert.Equal(boosted.Color("text.primary"), boosted.Color("reader.chrome.text"));           // dependents re-derive
        Assert.DoesNotContain(boosted.Warnings, w => w.PropertyPath == "text.primary");
        Assert.Equal(C("#4DA3FF"), boosted.Color("action.primary"));                                  // only text roles are touched
    }

    [Fact]
    public void BoostContrast_LeavesAnAlreadyReadableThemeExactlyAsItWas()
    {
        string json = File.ReadAllText(Path.Combine(SkinsFolder(), "default", "theme.json"));
        var document = Parse(json, out _, "default");

        var normal = ThemeResolver.Resolve(document, null);
        var boosted = ThemeResolver.Resolve(document, null, new UserThemeOverrides { BoostContrast = true });

        Assert.Equal(normal.Color("text.primary"), boosted.Color("text.primary"));
        Assert.Equal(normal.Color("text.secondary"), boosted.Color("text.secondary"));
        Assert.Equal(normal.Color("text.accent"), boosted.Color("text.accent"));
    }

    // ===================== Package-relative files =====================

    [Theory]
    [InlineData("layouts/home.json", true)]
    [InlineData("assets\\icons\\home.svg", true)]
    [InlineData("../outside.json", false)]
    [InlineData("layouts/../../x.json", false)]
    [InlineData("/etc/passwd", false)]
    [InlineData("C:/Windows/x.json", false)]
    [InlineData("https://example.com/x.png", false)]
    [InlineData("", false)]
    public void PackagePaths_OnlyAcceptPathsThatStayInsideThePackage(string path, bool expected)
        => Assert.Equal(expected, ThemePackagePaths.TryNormalize(path, out _));

    [Fact]
    public void ALayoutFile_IsLoadedFromThePackage_AndOneOutsideItIsRefused()
    {
        var files = new Dictionary<string, string> { ["layouts/home.json"] = """{ "root": { "id": "r", "type": "Stack" } }""" };
        var document = Parse(
            """{ "schemaVersion": 2, "name": "L", "base": { "layouts": { "Home": "layouts/home.json", "Library": "../steal.json", "Detail": "layouts/missing.json" } } }""",
            out var diagnostics, "l", path => files.TryGetValue(path, out var text) ? text : null);

        Assert.True(document.Base.Layouts.ContainsKey("Home"));
        Assert.False(document.Base.Layouts.ContainsKey("Library"));
        Assert.Contains(diagnostics, d => d.Code == ThemeDiagnosticCodes.UnsafePackage);
        Assert.Contains(diagnostics, d => d.Code == ThemeDiagnosticCodes.InvalidAsset);
    }
}
