using System.Text.Json.Nodes;
using Paperbunkr.App.Theming;
using Paperbunkr.App.Theming.Layout;
using Paperbunkr.App.Theming.Motion;
using Paperbunkr.App.Views.Home;

namespace Paperbunkr.App.Tests.Theming;

/// <summary>
/// The pure half of the layout engine and of motion (docs/superpowers/specs/2026-10-12-theme-system-v2-gap-analysis-and-plan.md, phases 4 and 5):
/// what a theme's layout and motion JSON may and may not say. No controls are created here.
/// </summary>
public class ThemeLayoutValidatorTests
{
    private static readonly ThemeLayoutContract Contract = HomeLayoutContract.Instance;

    private const string Minimal = """
        { "root": { "id": "root", "type": "Stack", "children": [
            { "id": "masthead", "type": "Home.Masthead" },
            { "id": "sections", "type": "Home.Sections" } ] } }
        """;

    private static ThemeLayoutDocument? Validate(string json, out List<ThemeDiagnostic> diagnostics, IReadOnlyDictionary<string, JsonObject>? patches = null)
    {
        diagnostics = new List<ThemeDiagnostic>();
        return ThemeLayoutValidator.Validate((JsonObject)JsonNode.Parse(json)!, Contract, diagnostics, patches);
    }

    private static string Root(string children, string extra = "") =>
        $$"""{ "root": { "id": "root", "type": "Stack", {{extra}} "children": [ { "id": "m", "type": "Home.Masthead" }, { "id": "s", "type": "Home.Sections" } {{(children.Length > 0 ? "," + children : "")}} ] } }""";

    // ===================== Structure =====================

    [Fact]
    public void AValidLayout_BecomesATree_WithItsPropertiesTyped()
    {
        var document = Validate("""
            { "contract": 1, "root": { "id": "root", "type": "Dock", "children": [
                { "id": "side", "type": "Panel", "dock": "left", "width": 320, "background": "surface.raised", "padding": "16,12", "children": [ { "id": "masthead", "type": "Home.Masthead" } ] },
                { "id": "main", "type": "Scroll", "dock": "fill", "children": [ { "id": "sections", "type": "Home.Sections", "margin": 24 } ] } ] } }
            """, out var diagnostics);

        Assert.NotNull(document);
        Assert.Empty(diagnostics);
        Assert.Equal("Home", document!.Screen);
        Assert.Equal(new[] { "root", "side", "masthead", "main", "sections" }, document.Root.SelfAndDescendants().Select(n => n.Id));
        var side = document.Root.Children[0];
        Assert.Equal(320, side.Number("width"));
        Assert.Equal("left", side.Text("dock"));
        Assert.Equal("surface.raised", side.Text("background"));
    }

    [Theory]
    [InlineData("""{ "nothing": true }""", "root")]
    [InlineData("""{ "root": { "type": "Stack" } }""", "id")]
    [InlineData("""{ "root": { "id": "has space", "type": "Stack" } }""", "id")]
    [InlineData("""{ "root": { "id": "root", "type": "WebView" } }""", "not a component")]
    [InlineData("""{ "root": { "id": "root", "type": "System.Diagnostics.Process" } }""", "not a component")]
    public void AStructuralProblem_RefusesTheWholeLayout(string json, string expectedInMessage)
    {
        var document = Validate(json, out var diagnostics);

        Assert.Null(document);
        var error = Assert.Single(diagnostics, d => d.Severity == ThemeDiagnosticSeverity.Error);
        Assert.Contains(expectedInMessage, error.Message);
        Assert.True(error.FallbackUsed);
        Assert.Equal(ThemeDiagnosticCodes.InvalidLayout, error.Code);
    }

    [Fact]
    public void DuplicateIds_AreRefused()
    {
        Assert.Null(Validate(Root("""{ "id": "m", "type": "Spacer" }"""), out var diagnostics));
        Assert.Contains(diagnostics, d => d.Message.Contains("'m' is used 2 times"));
    }

    [Fact]
    public void ATreeThatIsTooDeep_IsRefused()
    {
        string nested = """{ "id": "leaf", "type": "Spacer" }""";
        for (int i = 0; i < ThemeLayoutVocabulary.MaxDepth + 1; i++)
        {
            nested = $$"""{ "id": "p{{i}}", "type": "Panel", "children": [ {{nested}} ] }""";
        }

        Assert.Null(Validate(Root(nested), out var diagnostics));
        Assert.Contains(diagnostics, d => d.Message.Contains("levels deep"));
    }

    [Fact]
    public void TooManyChildren_AndTooManyNodes_AreRefused()
    {
        string many = string.Join(",", Enumerable.Range(0, ThemeLayoutVocabulary.MaxChildren + 1).Select(i => $$"""{ "id": "c{{i}}", "type": "Spacer" }"""));
        Assert.Null(Validate($$"""{ "root": { "id": "root", "type": "Stack", "children": [ {{many}} ] } }""", out var diagnostics));
        Assert.Contains(diagnostics, d => d.Message.Contains("children; the limit"));

        // 6 stacks of 39 spacers each = 240 nodes, every stack within its own child limit.
        string stacks = string.Join(",", Enumerable.Range(0, 6).Select(s =>
            $$"""{ "id": "s{{s}}", "type": "Stack", "children": [ {{string.Join(",", Enumerable.Range(0, 39).Select(i => $$"""{ "id": "s{{s}}c{{i}}", "type": "Spacer" }"""))}} ] }"""));
        Assert.Null(Validate(Root(stacks), out diagnostics));
        Assert.Contains(diagnostics, d => d.Message.Contains("nodes; the limit"));
    }

    [Theory]
    [InlineData("""{ "id": "split", "type": "Split", "children": [ { "id": "only", "type": "Spacer" } ] }""", "exactly 2")]
    [InlineData("""{ "id": "text", "type": "Text", "children": [ { "id": "inner", "type": "Spacer" } ] }""", "no children")]
    [InlineData("""{ "id": "scroll", "type": "Scroll" }""", "exactly 1")]
    public void AContainer_WithTheWrongNumberOfChildren_IsRefused(string child, string expected)
    {
        Assert.Null(Validate(Root(child), out var diagnostics));
        Assert.Contains(diagnostics, d => d.Message.Contains(expected));
    }

    [Fact]
    public void AHostComponent_CannotHaveChildren()
    {
        var document = Validate("""{ "root": { "id": "root", "type": "Stack", "children": [ { "id": "m", "type": "Home.Masthead", "children": [ { "id": "x", "type": "Spacer" } ] }, { "id": "s", "type": "Home.Sections" } ] } }""", out var diagnostics);

        Assert.Null(document);
        Assert.Contains(diagnostics, d => d.Message.Contains("no children"));
    }

    // ===================== The screen stays usable =====================

    [Theory]
    [InlineData("""{ "root": { "id": "root", "type": "Stack", "children": [ { "id": "s", "type": "Home.Sections" } ] } }""", "Home.Masthead or Home.Search")]
    [InlineData("""{ "root": { "id": "root", "type": "Stack", "children": [ { "id": "m", "type": "Home.Masthead" } ] } }""", "Home.Sections or Home.Section")]
    public void ALayoutThatLeavesOutSomethingTheScreenNeeds_IsRefused(string json, string expected)
    {
        Assert.Null(Validate(json, out var diagnostics));
        Assert.Contains(diagnostics, d => d.Message.Contains(expected));
    }

    [Fact]
    public void SearchAlone_AndSingleSections_SatisfyTheRequirements()
    {
        var document = Validate("""
            { "root": { "id": "root", "type": "Stack", "children": [
                { "id": "search", "type": "Home.Search" },
                { "id": "continue", "type": "Home.Section", "section": "continueReading" },
                { "id": "recent", "type": "Home.Section", "section": "recentlyAdded" } ] } }
            """, out var diagnostics);

        Assert.NotNull(document);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void AUniqueHostComponent_CanAppearOnlyOnce()
    {
        Assert.Null(Validate(Root("""{ "id": "again", "type": "Home.Sections" }"""), out var diagnostics));
        Assert.Contains(diagnostics, d => d.Message.Contains("Home.Sections can appear only once"));
    }

    [Fact]
    public void TheSameSection_CannotBePlacedTwice_AndASectionNeedsARealKey()
    {
        Assert.Null(Validate(Root("""{ "id": "a", "type": "Home.Section", "section": "spotlight" }, { "id": "b", "type": "Home.Section", "section": "spotlight" }"""), out var twice));
        Assert.Contains(twice, d => d.Message.Contains("'spotlight' can appear only once"));

        Assert.Null(Validate(Root("""{ "id": "a", "type": "Home.Section", "section": "notASection" }"""), out var unknown));
        Assert.Contains(unknown, d => d.Message.Contains("is not one of"));
        Assert.Contains(unknown, d => d.Message.Contains("needs a \"section\""));
    }

    // ===================== Properties: a small mistake costs one property, not the layout =====================

    [Fact]
    public void AnUnknownProperty_IsIgnoredWithAWarning()
    {
        var document = Validate(Root("", "\"onClick\": \"doEvil()\", \"spacing\": 12,"), out var diagnostics);

        Assert.NotNull(document);
        Assert.Null(document!.Root.Raw("onClick"));
        Assert.Equal(12, document.Root.Number("spacing"));
        Assert.Contains(diagnostics, d => d.Severity == ThemeDiagnosticSeverity.Warning && d.Message.Contains("'onClick' is not a property of Stack"));
    }

    [Theory]
    [InlineData("\"spacing\": \"wide\",", "spacing")]
    [InlineData("\"spacing\": -4,", "spacing")]
    [InlineData("\"opacity\": 3,", "opacity")]
    [InlineData("\"orientation\": \"diagonal\",", "orientation")]
    [InlineData("\"margin\": \"1,2,3\",", "margin")]
    [InlineData("\"visible\": \"yes\",", "visible")]
    public void ABadValue_DropsThatProperty_AndKeepsTheLayout(string property, string name)
    {
        var document = Validate(Root("", property), out var diagnostics);

        Assert.NotNull(document);
        Assert.Null(document!.Root.Raw(name));
        Assert.Contains(diagnostics, d => d.PropertyPath!.EndsWith("." + name));
    }

    [Fact]
    public void Text_MayOnlyShowPublishedValues_AndAButtonMayOnlyRunPublishedCommands()
    {
        var document = Validate(Root("""
            { "id": "hello", "type": "Text", "text": "{home.greeting}, welcome to {app.name}" },
            { "id": "leak", "type": "Text", "text": "{viewModel.ConnectionString}" },
            { "id": "ok", "type": "Button", "text": "Refresh", "command": "home.refresh" },
            { "id": "bad", "type": "Button", "text": "Format", "command": "system.formatDrive" }
            """), out var diagnostics);

        Assert.NotNull(document);
        var nodes = document!.Root.SelfAndDescendants().ToDictionary(n => n.Id);
        Assert.Equal("{home.greeting}, welcome to {app.name}", nodes["hello"].Text("text"));
        Assert.Null(nodes["leak"].Text("text"));
        Assert.Equal("home.refresh", nodes["ok"].Text("command"));
        Assert.Null(nodes["bad"].Text("command"));
        Assert.Contains(diagnostics, d => d.Severity == ThemeDiagnosticSeverity.Error && d.Message.Contains("viewModel.ConnectionString"));
        Assert.Contains(diagnostics, d => d.Severity == ThemeDiagnosticSeverity.Error && d.Message.Contains("system.formatDrive"));
    }

    [Fact]
    public void Conditions_AreALiteral_ABinding_OrItsNegation_AndNothingElse()
    {
        var document = Validate(Root("""
            { "id": "a", "type": "Spacer", "visible": false },
            { "id": "b", "type": "Spacer", "visible": { "bind": "home.hasSections" } },
            { "id": "c", "type": "Spacer", "visible": { "not": "home.hasSections" } },
            { "id": "d", "type": "Spacer", "visible": { "bind": "secret.flag" } },
            { "id": "e", "type": "Spacer", "visible": { "expr": "1 == 1" } }
            """), out _);

        var nodes = document!.Root.SelfAndDescendants().ToDictionary(n => n.Id);
        Assert.NotNull(nodes["a"].Raw("visible"));
        Assert.NotNull(nodes["b"].Raw("visible"));
        Assert.NotNull(nodes["c"].Raw("visible"));
        Assert.Null(nodes["d"].Raw("visible"));
        Assert.Null(nodes["e"].Raw("visible"));
    }

    [Fact]
    public void AnImage_MayOnlyComeFromInsideTheThemePackage()
    {
        var document = Validate(Root("""
            { "id": "ok", "type": "Image", "source": "assets/images/banner.png" },
            { "id": "remote", "type": "Image", "source": "https://example.com/tracker.png" },
            { "id": "escape", "type": "Image", "source": "../../secrets.png" }
            """), out _);

        var nodes = document!.Root.SelfAndDescendants().ToDictionary(n => n.Id);
        Assert.Equal("assets/images/banner.png", nodes["ok"].Text("source"));
        Assert.Null(nodes["remote"].Text("source"));
        Assert.Null(nodes["escape"].Text("source"));
    }

    [Fact]
    public void ALayoutWrittenForANewerContract_IsRefused()
    {
        Assert.Null(Validate("""{ "contract": 99, "root": { "id": "root", "type": "Stack" } }""", out var diagnostics));
        Assert.Contains(diagnostics, d => d.Message.Contains("newer version"));
    }

    // ===================== Breakpoints and user patches =====================

    private const string WithBreakpoints = """
        { "root": { "id": "root", "type": "Stack", "spacing": 24, "children": [ { "id": "m", "type": "Home.Masthead" }, { "id": "s", "type": "Home.Sections", "margin": 24 } ] },
          "breakpoints": {
            "compact": { "maxWidth": 720, "overrides": { "root": { "spacing": 8 }, "s": { "margin": 8, "type": "Text" }, "ghost": { "margin": 1 } } },
            "narrow": { "maxWidth": 900, "overrides": { "root": { "spacing": 16 } } },
            "always": { "overrides": { "root": { "spacing": 1 } } },
            "impossible": { "minWidth": 900, "maxWidth": 100 } } }
        """;

    [Fact]
    public void ABreakpoint_OverridesProperties_OfTheNodesItNames_AndTheFirstMatchWins()
    {
        var document = Validate(WithBreakpoints, out var diagnostics);

        Assert.NotNull(document);
        Assert.Equal(new[] { "compact", "narrow" }, document!.Breakpoints.Select(b => b.Name));
        Assert.Null(document.BreakpointFor(1400));
        Assert.Equal("narrow", document.BreakpointFor(800)!.Name);
        Assert.Equal("compact", document.BreakpointFor(600)!.Name);                 // both match; declared first wins

        var compact = document.RootFor(document.BreakpointFor(600));
        Assert.Equal(8, compact.Number("spacing"));
        Assert.Equal(8, compact.Children[1].Number("margin"));
        Assert.Equal("Home.Sections", compact.Children[1].Type);                     // an override cannot change what a node is
        Assert.Equal(24, document.Root.Number("spacing"));                           // the base tree is untouched

        Assert.Contains(diagnostics, d => d.Message.Contains("cannot change 'type'"));
        Assert.Contains(diagnostics, d => d.Message.Contains("'ghost' no longer matches"));
        Assert.Contains(diagnostics, d => d.Message.Contains("'always' has neither"));
        Assert.Contains(diagnostics, d => d.Message.Contains("'impossible'"));
        Assert.Contains(diagnostics, d => d.Severity == ThemeDiagnosticSeverity.Information && d.Message.Contains("overlap"));
    }

    [Fact]
    public void AUserPatch_IsAppliedByNodeId_AndOneThatNoLongerFits_IsDroppedAndReported()
    {
        var patches = new Dictionary<string, JsonObject>
        {
            ["s"] = new() { ["margin"] = 48, ["visible"] = false },
            ["removedInAnUpdate"] = new() { ["margin"] = 1 },
            ["m"] = new() { ["id"] = "renamed" },
        };

        var document = Validate(Minimal.Replace("\"sections\"", "\"s\"").Replace("\"masthead\"", "\"m\""), out var diagnostics, patches);

        Assert.NotNull(document);
        var sections = document!.Root.Children[1];
        Assert.Equal(48, sections.Number("margin"));
        Assert.False(sections.Flag("visible"));
        Assert.Equal("m", document.Root.Children[0].Id);
        Assert.Contains(diagnostics, d => d.Message.Contains("'removedInAnUpdate' no longer matches"));
        Assert.Contains(diagnostics, d => d.Message.Contains("cannot change 'id'"));
    }

    // ===================== Motion definitions =====================

    private static ThemeMotionDefinition? Motion(string json, out List<ThemeDiagnostic> diagnostics)
    {
        diagnostics = new List<ThemeDiagnostic>();
        return ThemeMotionDefinition.Parse("test", (JsonObject)JsonNode.Parse(json)!, diagnostics);
    }

    [Fact]
    public void AValidMotion_ParsesItsTimeline()
    {
        var motion = Motion("""
            { "trigger": "load", "duration": 400, "delay": 80, "easing": "backOut", "iterations": 2, "then": "fadeIn",
              "keyframes": [ { "at": 0, "opacity": 0, "translateY": 20, "scale": 0.9 }, { "at": 0.6, "opacity": 1 }, { "at": 1, "opacity": 1, "translateY": 0, "scale": 1 } ] }
            """, out var diagnostics);

        Assert.NotNull(motion);
        Assert.Empty(diagnostics);
        Assert.Equal(TimeSpan.FromMilliseconds(400), motion!.Duration);
        Assert.Equal(TimeSpan.FromMilliseconds(80), motion.Delay);
        Assert.Equal(2, motion.Iterations);
        Assert.Equal("fadeIn", motion.Then);
        Assert.Equal(3, motion.Keyframes.Count);
        Assert.True(motion.AnimatesOpacity && motion.AnimatesTranslation && motion.AnimatesScale);
        Assert.False(motion.AnimatesRotation);
    }

    [Theory]
    [InlineData("pressed", ThemeMotionTrigger.Pressed)]
    [InlineData("selected", ThemeMotionTrigger.Selected)]
    [InlineData("enter", ThemeMotionTrigger.Enter)]
    [InlineData("exit", ThemeMotionTrigger.Exit)]
    public void Motion_ParsesCoreStateTriggers(string trigger, ThemeMotionTrigger expected)
    {
        var motion = Motion($$"""{ "trigger": "{{trigger}}", "keyframes": [ { "at": 0, "opacity": 0 }, { "at": 1, "opacity": 1 } ] }""", out var diagnostics);

        Assert.NotNull(motion);
        Assert.Empty(diagnostics);
        Assert.Equal(expected, motion!.Trigger);
    }

    [Theory]
    [InlineData("""{ "keyframes": [ { "at": 0, "width": 10 }, { "at": 1 } ] }""", "'width'")]
    [InlineData("""{ "keyframes": [ { "at": 0, "RenderTransform": "x" }, { "at": 1 } ] }""", "'RenderTransform'")]
    [InlineData("""{ "script": "alert(1)", "keyframes": [ { "at": 0 }, { "at": 1 } ] }""", "unknown setting 'script'")]
    [InlineData("""{ "keyframes": [ { "at": 0, "opacity": 0 } ] }""", "between 2 and")]
    [InlineData("""{ "keyframes": [ { "at": 0, "opacity": 5 }, { "at": 1, "opacity": 1 } ] }""", "out of range")]
    [InlineData("""{ "keyframes": [ { "at": 0.2, "opacity": 0 }, { "at": 1, "opacity": 1 } ] }""", "starting at 0")]
    [InlineData("""{ "keyframes": [ { "at": 0, "opacity": 0 }, { "at": 1, "opacity": 0.5 } ] }""", "end at rest")]
    [InlineData("""{ "keyframes": [ { "at": 0, "translateX": 0 }, { "at": 1, "translateX": 40 } ] }""", "end at rest")]
    [InlineData("""{ "duration": 5, "keyframes": [ { "at": 0, "opacity": 0 }, { "at": 1, "opacity": 1 } ] }""", "duration between")]
    [InlineData("""{ "duration": 999999, "keyframes": [ { "at": 0, "opacity": 0 }, { "at": 1, "opacity": 1 } ] }""", "duration between")]
    [InlineData("""{ "easing": "wobble", "keyframes": [ { "at": 0, "opacity": 0 }, { "at": 1, "opacity": 1 } ] }""", "easing 'wobble'")]
    [InlineData("""{ "trigger": "everyFrame", "keyframes": [ { "at": 0, "opacity": 0 }, { "at": 1, "opacity": 1 } ] }""", "trigger 'everyFrame'")]
    [InlineData("""{ "trigger": "exit", "duration": 900, "keyframes": [ { "at": 0, "opacity": 0 }, { "at": 1, "opacity": 1 } ] }""", "exit duration longer than")]
    [InlineData("""{ "iterations": 5000, "keyframes": [ { "at": 0, "opacity": 0 }, { "at": 1, "opacity": 1 } ] }""", "iterations between")]
    [InlineData("""{ "iterations": "infinite", "keyframes": [ { "at": 0, "translateY": 0 }, { "at": 0.5, "translateY": 8 }, { "at": 1, "translateY": 0 } ] }""", "continuous and moves")]
    public void AnUnsafeOrMalformedMotion_IsRefused_WithAReason(string json, string expected)
    {
        var motion = Motion(json, out var diagnostics);

        Assert.Null(motion);
        var error = Assert.Single(diagnostics);
        Assert.Equal(ThemeDiagnosticCodes.InvalidMotion, error.Code);
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void AContinuousMotion_MayBreathe_ButNotMove()
    {
        var motion = Motion("""{ "iterations": "infinite", "duration": 2400, "easing": "sineInOut", "keyframes": [ { "at": 0, "opacity": 1 }, { "at": 0.5, "opacity": 0.7 }, { "at": 1, "opacity": 1 } ] }""", out _);

        Assert.NotNull(motion);
        Assert.True(motion!.IsContinuous);
    }

    [Fact]
    public void ThePresets_AreAllValidMotionsThatEndAtRest()
    {
        Assert.Equal(new[] { "fadeIn", "fadeUp", "scaleIn" }, ThemeMotionDefinition.Presets.Keys.OrderBy(k => k));
        foreach (var preset in ThemeMotionDefinition.Presets.Values)
        {
            var last = preset.Keyframes[^1];
            Assert.Equal(1, last.Opacity ?? 1);
            Assert.Equal(0, last.TranslateY ?? 0);
            Assert.Equal(1, last.Scale ?? 1);
            Assert.False(preset.IsContinuous);
        }
    }

    [Theory]
    [InlineData("12", true)]
    [InlineData("8,4", true)]
    [InlineData("1,2,3,4", true)]
    [InlineData("1,2,3", false)]
    [InlineData("-5", false)]
    [InlineData("a,b", false)]
    public void Thickness_AcceptsOneTwoOrFourNonNegativeNumbers(string text, bool expected)
        => Assert.Equal(expected, ThemeLayoutVocabulary.TryParseThickness(JsonValue.Create(text)!, out _));
}
