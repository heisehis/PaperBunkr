using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Paperbunkr.App.Tests.Theming;

/// <summary>
/// Keeps the 2026-10-19 UI polish foundation from drifting back (docs/superpowers/specs/2026-10-19-ui-polish-foundation-design.md §8).
/// The app looked "slightly out of line everywhere" because views set their own sizes, radii and text colours by hand - 29 different font
/// sizes, 13 different radii that ignored the theme, a faint text colour too dim to read. Each rule below names the file and line to fix.
/// Icons are exempt from the font-size rule: a glyph's FontSize is its icon size (the PbIconSize* scale), tidied in sub-project 3.
/// </summary>
public partial class XamlConsistencyLintTests
{
    /// <summary>The type scale (design Q7): Micro 10 · Caption 11 · Meta 12 · Body 13 · Section 15 · Title 18 · Heading 22; 24 and up is display text.</summary>
    private static readonly HashSet<double> TypeScale = [10, 11, 12, 13, 15, 18, 22];

    private static readonly string[] IconElements = ["fi:SymbolIcon", "fi:FluentIcon", "PathIcon", "SymbolIcon", "controls:ThemedIcon", "c:ThemedIcon"];

    [GeneratedRegex(@"(?<![\w.])FontSize=""(?<v>[0-9]+(?:\.[0-9]+)?)""")]
    private static partial Regex FontSizeAttribute();

    [GeneratedRegex(@"<Setter\s+Property=""FontSize""\s+Value=""(?<v>[0-9]+(?:\.[0-9]+)?)""")]
    private static partial Regex FontSizeSetter();

    [GeneratedRegex(@"(?<![\w.])CornerRadius=""(?<v>[0-9]+(?:\.[0-9]+)?)""|<Setter\s+Property=""CornerRadius""\s+Value=""(?<v>[0-9]+(?:\.[0-9]+)?)""")]
    private static partial Regex UniformCornerRadius();

    [GeneratedRegex(@"\{StaticResource (PbRadius|PbRadiusSm|PbRadiusLg)\}")]
    private static partial Regex StaticThemeRadius();

    [GeneratedRegex(@"<(?:TextBlock|SelectableTextBlock|Run)\b[^>]*Foreground=""\{(?:Dynamic|Static)Resource PbTextFaintBrush\}""", RegexOptions.Singleline)]
    private static partial Regex FaintText();

    [Fact]
    public void FontSizesAreOnTheTypeScale()
    {
        var offenders = new List<string>();
        foreach (var (relative, xaml) in AppXaml())
        {
            foreach (Match m in FontSizeAttribute().Matches(xaml))
            {
                double size = double.Parse(m.Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture);
                if (size >= 24 || TypeScale.Contains(size) || IconElements.Contains(EnclosingElement(xaml, m.Index)))
                {
                    continue;
                }

                offenders.Add($"{relative}:{Line(xaml, m.Index)} FontSize {size}");
            }

            foreach (Match m in FontSizeSetter().Matches(xaml))
            {
                double size = double.Parse(m.Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture);
                if (size >= 24 || TypeScale.Contains(size) || IconElements.Any(icon => EnclosingSelector(xaml, m.Index).Contains(icon.Split(':')[^1], StringComparison.Ordinal)))
                {
                    continue;
                }

                offenders.Add($"{relative}:{Line(xaml, m.Index)} Setter FontSize {size}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Font sizes off the type scale (10, 11, 12, 13, 15, 18, 22, or 24+). Use the nearest step or a pbText* role class:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void CornerRadiiFollowTheTheme()
    {
        var offenders = new List<string>();
        foreach (var (relative, xaml) in AppXaml())
        {
            foreach (Match m in UniformCornerRadius().Matches(xaml))
            {
                double radius = double.Parse(m.Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture);
                if (radius <= 2)
                {
                    continue; // hairlines and progress tracks a few pixels tall: a theme radius would turn them into pills
                }

                if (IsCircle(xaml, m.Index, radius))
                {
                    continue; // a round icon button: Width == Height == 2 x radius on the same element or style
                }

                offenders.Add($"{relative}:{Line(xaml, m.Index)} CornerRadius {radius}");
            }

            foreach (Match m in StaticThemeRadius().Matches(xaml))
            {
                offenders.Add($"{relative}:{Line(xaml, m.Index)} {m.Value} (themes rewrite this key; StaticResource never sees it)");
            }
        }

        Assert.True(offenders.Count == 0,
            "Corner radii that ignore the theme. Use {DynamicResource PbRadiusSm|PbRadius|PbRadiusLg|PbRadiusChip}:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void ReadableTextIsNotFaint()
    {
        var offenders = new List<string>();
        foreach (var (relative, xaml) in AppXaml())
        {
            foreach (Match m in FaintText().Matches(xaml))
            {
                offenders.Add($"{relative}:{Line(xaml, m.Index)}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Text drawn in PbTextFaintBrush (3.1-3.8:1 in most themes, too dim to read). Use PbTextMutedBrush; faint is for disabled states, " +
            "dividers and decoration only:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void FieldLabelIsDefinedOnlyInSharedStyles()
    {
        var offenders = AppXaml()
            .Where(f => !f.Relative.StartsWith("Styles/", StringComparison.Ordinal))
            .Where(f => Regex.IsMatch(f.Xaml, @"Selector=""TextBlock\.fieldLabel""(?=[^>]*>\s*<Setter Property=""(FontSize|Foreground|FontWeight)"")"))
            .Select(f => f.Relative)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These views restyle TextBlock.fieldLabel's look locally; the shared one is in Styles/FormControls.axaml:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    // ---------------- Shared control families (docs/superpowers/specs/2026-10-19-ui-polish-shared-controls-design.md) ----------------

    /// <summary>Class names folded into the shared families; using one again brings back a private copy of a control.</summary>
    private static readonly HashSet<string> RetiredClasses =
    [
        "headerAction", "modeOption", "segTab", "ecTab", "lensTab", "ipTab", "detailsSubTab", "activityTab", "coverPickerTab", "ksToggle",
        "modeToggle", "ksSeg", "groupBox", "sectionCard", "rowCard", "ksPanel", "groupCard", "reviewCard", "miniCard", "issueCard", "statCard",
        "toolbarPill", "pillOption", "sheetOption", "chapterFilterPill", "rangeChip", "rlChip", "chipBtn", "tagPill", "previewChip", "valueChip",
        "heroChip", "bandChip", "vtChip", "ksChip", "changelogTag", "historyTag", "contrastChip", "scheduledTaskBadge", "currentBadge",
        "discardConfirm", "tplCard", "setupCard", "connTile", "continueCard",
    ];

    /// <summary>(element, class) pairs whose look lives only in Styles/Controls.axaml.</summary>
    private static readonly string[] FamilySelectors =
    [
        "Button.primary", "Button.secondary", "Button.ghost", "Button.danger", "Button.iconButton", "Button.segItem", "Button.tab",
        "Button.sideItemButton", "Button.prefNavItem", "Button.filterChip", "ToggleButton.filterChip", "Button.clickCard", "Button.menuRow",
        "Border.segGroup", "Border.pbChip", "Border.statusBadge", "Border.card", "Border.dropdown", "Border.tabStrip",
    ];

    /// <summary>Properties that make up a family's look. A view may still position a family control (Margin, Width, alignment) locally.</summary>
    private static readonly HashSet<string> FamilyVisualProperties =
    [
        "Background", "BorderBrush", "BorderThickness", "CornerRadius", "Padding", "FontSize", "FontWeight", "Foreground", "Height", "MinHeight",
    ];

    [Fact]
    public void RetiredControlClassesAreNotUsed()
    {
        var offenders = new List<string>();
        foreach (var (relative, xaml) in AppXaml())
        {
            foreach (Match m in Regex.Matches(xaml, @"Classes=""([^""]*)""|Selector=""([^""]*)"""))
            {
                var tokens = m.Groups[1].Success
                    ? m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    : Regex.Matches(m.Groups[2].Value, @"\.([\w-]+)").Select(t => t.Groups[1].Value);
                foreach (string token in tokens.Where(RetiredClasses.Contains))
                {
                    offenders.Add($"{relative}:{Line(xaml, m.Index)} {token}");
                }
            }

            foreach (Match m in Regex.Matches(xaml, @"<Button\b[^>]*\sClasses=""(?:[^""]*\s)?seg(?:\s[^""]*)?""", RegexOptions.Singleline))
            {
                offenders.Add($"{relative}:{Line(xaml, m.Index)} seg");
            }
        }

        Assert.True(offenders.Count == 0,
            "Retired control classes. Use the shared family in Styles/Controls.axaml (button, segItem/segGroup, tab, sideItemButton, " +
            "filterChip/pbChip/statusBadge, card/clickCard, menuRow):" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void SharedFamiliesAreNotRestyledLocally()
    {
        var offenders = new List<string>();
        foreach (var (relative, xaml) in AppXaml().Where(f => !f.Relative.StartsWith("Styles/", StringComparison.Ordinal)))
        {
            foreach (Match m in Regex.Matches(xaml, @"<Style Selector=""(?<sel>[^""]+)""\s*>(?<body>.*?)</Style>", RegexOptions.Singleline))
            {
                if (m.Groups["body"].Value.Contains("<Style", StringComparison.Ordinal))
                {
                    continue;
                }

                // Only selectors whose target is the family control itself (optionally a state or its own template part), not content inside it.
                bool targetsFamily = m.Groups["sel"].Value.Split(',').Any(part =>
                {
                    var target = Regex.Match(part.Trim(), @"^(?<el>\w+)(?<cls>(?:\.[\w-]+)+)(?::[\w-]+)*(?:\s*/template/\s*\S+)?$");
                    return target.Success && target.Groups["cls"].Value.Split('.', StringSplitOptions.RemoveEmptyEntries)
                        .Any(c => FamilySelectors.Contains($"{target.Groups["el"].Value}.{c}"));
                });
                if (!targetsFamily)
                {
                    continue;
                }

                var visual = Regex.Matches(m.Groups["body"].Value, @"<Setter\s+Property=""(\w+)""").Select(s => s.Groups[1].Value)
                    .Where(FamilyVisualProperties.Contains).Distinct().ToList();
                if (visual.Count > 0)
                {
                    offenders.Add($"{relative}:{Line(xaml, m.Index)} \"{m.Groups["sel"].Value}\" sets {string.Join(", ", visual)}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Views restyling a shared control family (its look belongs in Styles/Controls.axaml; a view may only position it):" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void FamilyButtonsCarryNoSizeOverrides()
    {
        string[] sized = ["primary", "secondary", "ghost", "danger", "segItem", "filterChip", "menuRow"];
        var offenders = new List<string>();
        foreach (var (relative, xaml) in AppXaml())
        {
            foreach (Match m in Regex.Matches(xaml, @"<(?:Button|ToggleButton)\b[^>]*>", RegexOptions.Singleline))
            {
                var classes = Regex.Match(m.Value, @"\sClasses=""([^""]*)""");
                if (!classes.Success || !classes.Groups[1].Value.Split(' ').Any(sized.Contains))
                {
                    continue;
                }

                var overrides = Regex.Matches(m.Value, @"\s(Padding|FontSize|Height|MinHeight|CornerRadius)=""(?!\{Binding)[^""]*""")
                    .Select(o => o.Groups[1].Value).ToList();
                if (overrides.Count > 0)
                {
                    offenders.Add($"{relative}:{Line(xaml, m.Index)} {string.Join(", ", overrides)}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Family buttons with their own size. Use .sm for the small size instead of Padding/FontSize/Height:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    // ---------------- Icons and accessible names (docs/superpowers/specs/2026-10-19-ui-polish-icons-a11y-design.md) ----------------

    /// <summary>Characters that were standing in for icons. In running text an arrow is punctuation and stays; as a button's content or a
    /// label on its own it is an icon and must be a FluentIcons glyph (text glyphs follow the text font, so they never line up).</summary>
    private const string GlyphIcons = "✕＋⋯▲▼✎▾▶⚙✓↻●★↔⠿🔖";

    [Fact]
    public void IconButtonsHaveNames()
    {
        var offenders = new List<string>();
        foreach (var (relative, xaml) in AppXaml())
        {
            foreach (Match m in Regex.Matches(xaml, @"<Button\b((?:[^>""]|""[^""]*"")*)>(.*?)</Button>", RegexOptions.Singleline))
            {
                string attrs = m.Groups[1].Value, body = m.Groups[2].Value;
                if (Regex.IsMatch(body + attrs, @"Text=|Content=|<TextBlock|<Run|<SelectableTextBlock|<ContentPresenter") ||
                    !Regex.IsMatch(body, @"(SymbolIcon|PathIcon|ThemedIcon|<Image|<Path|FluentIcon|BrandMark|<Ellipse)\b"))
                {
                    continue;
                }

                if (!attrs.Contains("AutomationProperties.Name", StringComparison.Ordinal))
                {
                    offenders.Add($"{relative}:{Line(xaml, m.Index)}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Icon-only buttons without AutomationProperties.Name (a screen reader only says \"button\"). Use the tooltip text or the action:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void NoTextGlyphIcons()
    {
        var offenders = new List<string>();
        foreach (var (relative, xaml) in AppXaml())
        {
            foreach (Match m in Regex.Matches(xaml, @"<(?<el>Button|TextBlock)\b[^>]*?\s(?:Content|Text)=""(?<v>[^""]*)"""))
            {
                string value = m.Groups["v"].Value.Trim();
                if (value.Length == 0)
                {
                    continue;
                }

                // An icon is the whole value, or its leading symbol ("＋ Add series"); a glyph later in a sentence is prose.
                bool whole = value.EnumerateRunes().All(r => GlyphIcons.Contains(r.ToString(), StringComparison.Ordinal) || Rune.IsWhiteSpace(r));
                bool leading = value.EnumerateRunes().First() is var first && GlyphIcons.Contains(first.ToString(), StringComparison.Ordinal);
                string format = Regex.Match(value, @"StringFormat='(?:\{\})?([^']*)'").Groups[1].Value;
                bool formatIcon = format.Length > 0 && format.EnumerateRunes().Any(r => GlyphIcons.Contains(r.ToString(), StringComparison.Ordinal))
                                  && !Regex.IsMatch(format, @"\p{L}{3,}");
                if (whole || leading || formatIcon)
                {
                    offenders.Add($"{relative}:{Line(xaml, m.Index)} {m.Groups["el"].Value} \"{value}\"");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Text characters used as icons. Use a fi:SymbolIcon (Dismiss, Add, MoreHorizontal, ChevronUp/Down, Edit, Play, Settings, Checkmark, " +
            "ArrowClockwise, Star, ArrowSwap, ReOrderDotsVertical):" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>A button that holds only an icon is an iconButton, not a text-button family (2026-10-20 on-screen check): ghost/secondary padding
    /// is sized for a label, so an icon in it is off-size, and squeezed to a fixed Width it was cropped (the Library folder rows).</summary>
    [Fact]
    public void IconOnlyButtonsAreNotTextButtons()
    {
        // A bordered secondary square beside other secondary buttons (Edit Issue's search + {} pair) is fine at its own padding; a fixed size is not.
        string[] textFamilies = ["ghost", "primary", "danger"];
        var offenders = new List<string>();
        foreach (var (relative, xaml) in AppXaml())
        {
            foreach (Match m in Regex.Matches(xaml,
                         @"<Button\b(?<a>[^>]*?)>\s*(?:<fi:SymbolIcon\b[^>]*/>|<Panel>\s*(?:<fi:SymbolIcon\b[^>]*/>\s*)+</Panel>)\s*</Button>"))
            {
                var classes = Regex.Match(m.Groups["a"].Value, @"Classes=""([^""]*)""").Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                bool sized = Regex.IsMatch(m.Groups["a"].Value, @"\s(?:Width|Height)=""\d");
                if (!classes.Contains("iconButton") && (classes.Intersect(textFamilies).Any() || (classes.Contains("secondary") && sized)))
                {
                    offenders.Add($"{relative}:{Line(xaml, m.Index)} [{string.Join(' ', classes)}]");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Icon-only buttons in a text-button family. Use Classes=\"iconButton\" (or \"iconButton sm\") with an AutomationProperties.Name:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>Every property transition names its easing (2026-10-20 UI polish SP5 Q2): a missing one runs linear, which reads as mechanical
    /// beside the app's PbMotionEase motion.</summary>
    [Fact]
    public void TransitionsHaveEasing()
    {
        var offenders = new List<string>();
        foreach (var (relative, xaml) in AppXaml())
        {
            foreach (Match m in Regex.Matches(xaml, @"<(?<el>\w+Transition)\b(?<attrs>[^>]*?\bProperty=""[^""]*""[^>]*?)(?<self>/?)>"))
            {
                if (m.Groups["attrs"].Value.Contains("Easing=", StringComparison.Ordinal))
                {
                    continue;
                }

                if (m.Groups["self"].Value.Length == 0)
                {
                    int close = xaml.IndexOf("</" + m.Groups["el"].Value + ">", m.Index, StringComparison.Ordinal);
                    if (close > 0 && xaml[m.Index..close].Contains(".Easing>", StringComparison.Ordinal))
                    {
                        continue;
                    }
                }

                offenders.Add($"{relative}:{Line(xaml, m.Index)} {m.Groups["el"].Value}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Transitions without an easing. Add Easing=\"{StaticResource PbMotionEase}\":" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    // ---- Motion v2 (docs/superpowers/specs/2026-10-20-motion-system-v2-foundation-overlays-design.md §6) ----

    private static readonly Regex OvershootEasing = new(@"\b(?:BackEase|ElasticEase|BounceEase)\w*", RegexOptions.Compiled);

    /// <summary>Overshoot-and-return curves wobble on every input; the one sanctioned overshoot is the damped spring (PbMotionSpring),
    /// which settles once.</summary>
    [Fact]
    public void NoOvershootEasings()
    {
        var offenders = new List<string>();
        foreach (var (relative, xaml) in AppXaml())
        {
            offenders.AddRange(OvershootEasing.Matches(xaml).Select(m => $"{relative}:{Line(xaml, m.Index)} {m.Value}"));
        }

        foreach (var (relative, code) in AppCode())
        {
            offenders.AddRange(Regex.Matches(code, @"new\s+(?:BackEase|ElasticEase|BounceEase)\w*").Select(m => $"{relative}:{Line(code, m.Index)} {m.Value}"));
        }

        Assert.True(offenders.Count == 0,
            "Overshooting easings. Use PbMotionSpring for a lift or panel arrival, else PbMotionEase / PbMotionExitEase / PbMotionMoveEase:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>Transitioning BoxShadow tears focus and glow rings mid-flight; animate the Opacity of a fixed-shadow layer instead.</summary>
    [Fact]
    public void NoBoxShadowsTransition()
    {
        var offenders = AppXaml().SelectMany(f => Regex.Matches(f.Xaml, "<BoxShadowsTransition").Select(m => $"{f.Relative}:{Line(f.Xaml, m.Index)}"))
            .Concat(AppCode().SelectMany(f => Regex.Matches(f.Code, @"new\s+BoxShadowsTransition").Select(m => $"{f.Relative}:{Line(f.Code, m.Index)}")))
            .ToList();

        Assert.True(offenders.Count == 0, "BoxShadowsTransition:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>A transition on a layout property relayouts every frame. Allowed only where content must reflow, with a comment containing
    /// "motion: reflow" and the reason, inside the same style block before the transition.</summary>
    [Fact]
    public void LayoutTransitionsJustified()
    {
        var offenders = new List<string>();
        foreach (var (relative, raw) in AppXamlRaw())
        {
            foreach (Match m in Regex.Matches(raw, @"<\w+Transition\b[^>]*\bProperty=""(?:Width|Height|MinWidth|MinHeight|MaxWidth|MaxHeight|Margin|Padding)"""))
            {
                int note = raw.LastIndexOf("motion: reflow", m.Index, StringComparison.Ordinal);
                bool justified = note >= 0 && raw.IndexOf("</Style>", note, m.Index - note, StringComparison.Ordinal) < 0;
                if (!justified)
                {
                    offenders.Add($"{relative}:{Line(raw, m.Index)}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Layout-property transitions without a \"motion: reflow\" justification comment in their style block:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>Every transition takes its duration and its curve from the motion tokens, so the policy (Reduced, Off, quality, the
    /// PB_MOTION_SCALE knob) reaches it.</summary>
    [Fact]
    public void TransitionTokensOnly()
    {
        var offenders = new List<string>();
        foreach (var (relative, xaml) in AppXaml())
        {
            foreach (Match m in Regex.Matches(xaml, @"<(?<el>\w+Transition)\b(?<attrs>[^>]*?\bProperty=""[^""]*""[^>]*?)/?>"))
            {
                string attrs = m.Groups["attrs"].Value;
                bool duration = Regex.IsMatch(attrs, @"\bDuration=""\{DynamicResource PbMotion\w+\}""");
                // The splash is built before (and in tests without) the app's resources, where a StaticResource curve throws; its duration
                // is still a DynamicResource token, which degrades gracefully.
                bool easing = Regex.IsMatch(attrs, @"\bEasing=""\{StaticResource PbMotion\w+\}""")
                    || (relative == "Views/SplashWindow.axaml" && attrs.Contains("Easing=", StringComparison.Ordinal));
                if (!duration || !easing)
                {
                    offenders.Add($"{relative}:{Line(xaml, m.Index)} {m.Groups["el"].Value}{(duration ? "" : " (duration)")}{(easing ? "" : " (easing)")}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Transitions must use Duration=\"{DynamicResource PbMotion*}\" and Easing=\"{StaticResource PbMotion*}\":" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static readonly HashSet<double> TranslateScale = [0, 1, 2, 3, 4, 6, 8, 12, 20, 24];

    /// <summary>Travel distances come from one scale (§3.3): 4 popups, 6 local, 8 toasts, 12 routes, 20 panels, 24 ambient, 1-3 nudges.</summary>
    [Fact]
    public void TranslateDistancesOnScale()
    {
        var offenders = new List<string>();
        foreach (var (relative, xaml) in AppXaml())
        {
            foreach (Match m in Regex.Matches(xaml, @"translate[XY]?\((?<args>[^)]*)\)"))
            {
                foreach (Match n in Regex.Matches(m.Groups["args"].Value, @"-?\d+(?:\.\d+)?"))
                {
                    double value = Math.Abs(double.Parse(n.Value, System.Globalization.CultureInfo.InvariantCulture));
                    if (!TranslateScale.Contains(value))
                    {
                        offenders.Add($"{relative}:{Line(xaml, m.Index)} {m.Value}");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Translate distances off the motion scale (1,2,3,4,6,8,12,20,24):" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static readonly string[] PressScaleFamilies =
        ["Button.primary", "Button.secondary", "Button.ghost", "Button.danger", "Button.iconButton", "Button.segItem", "Button.clickCard", "Button.card"];

    /// <summary>Only compact controls shrink when pressed (Q3); a wide row shrinking by several pixels reads as the row lurching.</summary>
    [Fact]
    public void PressScaleScoped()
    {
        var offenders = new List<string>();
        foreach (var (relative, xaml) in AppXaml())
        {
            foreach (Match m in Regex.Matches(xaml, @"Property=""RenderTransform""\s+Value=""scale\([^""]*\)"""))
            {
                string selector = EnclosingSelector(xaml, m.Index);
                foreach (string part in selector.Split(',').Select(p => p.Trim()).Where(p => p.Contains(":pressed", StringComparison.Ordinal)))
                {
                    if (!PressScaleFamilies.Any(f => part.StartsWith(f + ":", StringComparison.Ordinal) || part.StartsWith(f + ".", StringComparison.Ordinal)))
                    {
                        offenders.Add($"{relative}:{Line(xaml, m.Index)} {part}");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Press scale outside the compact families (" + string.Join(", ", PressScaleFamilies) + "):" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>Recorded 2026-10-20 when the role tokens arrived. PbMotionFast/Standard/Large/Slow are aliases now; new work uses the role
    /// tokens, and the count may only go down (lower this number when it does).</summary>
    private const int LegacyMotionTokenBaseline = 38;

    [Fact]
    public void LegacyMotionTokenBaselineOnlyGoesDown()
    {
        int count = AppXaml().Sum(f => Regex.Matches(f.Xaml, @"PbMotion(?:Fast|Standard|Large|Slow)\b").Count)
            + AppCode().Sum(f => Regex.Matches(f.Code, @"""PbMotion(?:Fast|Standard|Large|Slow)""").Count);

        Assert.True(count <= LegacyMotionTokenBaseline,
            $"{count} uses of the legacy PbMotionFast/Standard/Large/Slow tokens (baseline {LegacyMotionTokenBaseline}). Use the role tokens " +
            "(PbMotionAcknowledge, PbMotionHover*, PbMotionReveal*, PbMotionReplace, PbMotionTravel*, PbMotionFlight, PbMotionAmbient).");
    }

    private static IEnumerable<(string Relative, string Xaml)> AppXamlRaw()
    {
        string appRoot = AppRoot();
        foreach (string file in Directory.EnumerateFiles(appRoot, "*.axaml", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(appRoot, file).Replace('\\', '/');
            if (!relative.StartsWith("bin/", StringComparison.Ordinal) && !relative.StartsWith("obj/", StringComparison.Ordinal))
            {
                yield return (relative, File.ReadAllText(file));
            }
        }
    }

    /// <summary>App C# with // and /// comments blanked (line numbers kept).</summary>
    private static IEnumerable<(string Relative, string Code)> AppCode()
    {
        string appRoot = AppRoot();
        foreach (string file in Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(appRoot, file).Replace('\\', '/');
            if (!relative.StartsWith("bin/", StringComparison.Ordinal) && !relative.StartsWith("obj/", StringComparison.Ordinal))
            {
                yield return (relative, Regex.Replace(File.ReadAllText(file), @"//[^\n]*", string.Empty));
            }
        }
    }

    /// <summary>Not dialogs (a command palette, coach marks, first-run pages, a design sample) - or content hosted inside a DialogCard elsewhere
    /// (the scraper views live in MainWindow's plugin-modal DialogCard).</summary>
    private static readonly string[] NotDialogs =
    [
        "Views/QuickOpenOverlay.axaml", "Views/WelcomeTourOverlay.axaml", "Views/WelcomeOverlay.axaml", "Views/WhatsNewOverlay.axaml",
        "Views/DesignShowcaseOverlay.axaml",
    ];

    [Fact]
    public void DialogsUseDialogCard()
    {
        var offenders = AppXaml()
            .Where(f => (f.Relative.EndsWith("Overlay.axaml", StringComparison.Ordinal) || f.Relative == "Views/SmartTemplateGallery.axaml")
                        && !NotDialogs.Contains(f.Relative))
            .Where(f => !f.Xaml.Contains(":DialogCard", StringComparison.Ordinal))
            .Select(f => f.Relative)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Dialogs not built on controls:DialogCard (title, ✕ in the card, window-relative max height, scrolling body, footer):" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static IEnumerable<(string Relative, string Xaml)> AppXaml()
    {
        string appRoot = AppRoot();
        foreach (string file in Directory.EnumerateFiles(appRoot, "*.axaml", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(appRoot, file).Replace('\\', '/');
            if (relative.StartsWith("bin/", StringComparison.Ordinal) || relative.StartsWith("obj/", StringComparison.Ordinal) || relative == "Styles/AppTokens.axaml")
            {
                continue;
            }

            yield return (relative, StripComments(File.ReadAllText(file)));
        }
    }

    /// <summary>True when the element (attribute form) or the style block (setter form) holding this radius is a square of side 2 x radius.</summary>
    private static bool IsCircle(string xaml, int index, double radius)
    {
        string scope;
        if (xaml.AsSpan(index).StartsWith("<Setter"))
        {
            int start = xaml.LastIndexOf("<Style", index, StringComparison.Ordinal);
            int end = xaml.IndexOf("</Style>", index, StringComparison.Ordinal);
            scope = start >= 0 && end > start ? xaml[start..end] : string.Empty;
        }
        else
        {
            int start = xaml.LastIndexOf('<', index);
            int end = xaml.IndexOf('>', index);
            scope = start >= 0 && end > start ? xaml[start..end] : string.Empty;
        }

        string side = (radius * 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
        bool Has(string property) =>
            Regex.IsMatch(scope, $@"(?<![\w.]){property}=""{Regex.Escape(side)}""|Property=""{property}""\s+Value=""{Regex.Escape(side)}""");
        return Has("Width") && Has("Height");
    }

    private static string EnclosingElement(string xaml, int index)
    {
        int lt = xaml.LastIndexOf('<', index);
        var m = Regex.Match(xaml[lt..], @"^<([\w:.]+)");
        return m.Success ? m.Groups[1].Value : string.Empty;
    }

    private static string EnclosingSelector(string xaml, int index)
    {
        var matches = Regex.Matches(xaml[..index], @"<Style\b[^>]*Selector=""([^""]*)""");
        return matches.Count > 0 ? matches[^1].Groups[1].Value : string.Empty;
    }

    private static int Line(string xaml, int index) => xaml.AsSpan(0, index).Count('\n') + 1;

    /// <summary>Blanks comments but keeps their newlines, so reported line numbers stay right.</summary>
    private static string StripComments(string xaml) =>
        Regex.Replace(xaml, "<!--.*?-->", m => new string(m.Value.Where(c => c == '\n').ToArray()), RegexOptions.Singleline);

    private static string TestFolder([CallerFilePath] string caller = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(caller)!, ".."));

    private static string AppRoot() => Path.GetFullPath(Path.Combine(TestFolder(), "..", "Paperbunkr.App"));
}
