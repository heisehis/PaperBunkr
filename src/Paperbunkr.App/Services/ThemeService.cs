using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentAvalonia.Styling;
using Paperbunkr.App.Models;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using SkiaSharp;

namespace Paperbunkr.App.Services;

/// <summary>
/// Theme install-apply-persist mechanism (docs/superpowers/specs/2026-08-07-preferences-skin-system-design.md
/// §3, renamed from <c>SkinService</c> by docs/superpowers/specs/2026-09-16-theme-system-design.md -
/// same mechanism, extended with a Light/Dark <c>mode</c> that now drives
/// <see cref="Application.RequestedThemeVariant"/>). The built-in "default" theme is an embedded
/// <c>avares://</c> resource following the exact same <c>theme.json</c> schema as an installed
/// <c>.crpck</c> - switching themes and the app's own default look go through identical code, no
/// special-cased path. The <c>.crpck</c> install mechanism itself (<see cref="TryInstallSkin"/>/
/// <see cref="OpenSkinsFolder"/>) is deliberately untouched by this rename - deferred to a future
/// "skins" initiative per the design doc's own scope decision.
/// </summary>
public partial class ThemeService
{
    public const string DefaultThemeKey = "default";

    /// <summary>
    /// Raised after a theme's colors are live in <see cref="Application.Current"/>'s resources
    /// (docs/superpowers/specs/2026-09-08-home-navrail-visual-v2-design.md §3) - most consumers
    /// don't need this since their brushes are already <c>DynamicResource</c>-bound and Avalonia
    /// re-notifies them automatically, but a consumer that bakes a theme color into a raster (e.g.
    /// <c>HomeScreenViewModel</c>'s masthead cover-wall, built once via SkiaSharp rather than drawn
    /// live) has no other way to know a re-render is needed.
    /// </summary>
    public event Action? ThemeApplied;

    /// <summary>
    /// Raised alongside <see cref="ThemeApplied"/> with the newly-active theme's <c>windowBackdrop</c>
    /// (docs/superpowers/specs/2026-09-16-theme-system-design.md § Extended scope) - ThemeService has
    /// no window reference of its own, so the App composition root subscribes and sets
    /// <c>MainWindow.TransparencyLevelHint</c>, mirroring how <see cref="ScheduledThemeCrossfadeRequested"/>
    /// keeps ThemeService itself ignorant of the actual visual mechanics.
    /// </summary>
    public event Action<string>? WindowBackdropRequested;

    private readonly Dictionary<string, Bitmap> _iconCache = new();
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly Func<DateTimeOffset> _now;

    /// <summary>
    /// Whether the active theme is a light one (<c>mode: "Light"</c>), read live from the <c>PbThemeIsLight</c> resource
    /// <see cref="ApplyThemeResources"/> writes - Home's light-theme sky (docs/superpowers/specs/2026-09-28-home-cosmetics-design.md
    /// C2). False before the first apply and in hosts with no application.
    /// </summary>
    public static bool IsLightThemeActive
        => Application.Current is { } app && app.Resources.TryGetResource("PbThemeIsLight", null, out var value) && value is true;

    /// <summary>
    /// Fired right before a <see cref="ThemeAutoMode.Scheduled"/> switch applies the new theme's
    /// resources - the UI layer (<c>MainViewModel</c>) reacts by starting its own 300ms opacity dip
    /// (docs/superpowers/specs/2026-09-16-theme-system-design.md § Extended scope's "Graceful
    /// Auto-Theme Crossfades"). ThemeService deliberately has no knowledge of the actual XAML/visual
    /// mechanics - it just signals "about to swap", same separation <see cref="ThemeApplied"/>
    /// already keeps. Manual picks from the Appearance grid don't raise this - the crossfade is
    /// specific to the unattended Scheduled switch, where a jump-cut is more jarring since the user
    /// isn't looking at the picker when it happens.
    /// </summary>
    public event Action? ScheduledThemeCrossfadeRequested;

    /// <summary>Cheap lookup for consumers that only need the key, not the full theme list (e.g. <c>MainViewModel</c>'s Matrix-rain visibility check).</summary>
    public string GetActiveThemeKey()
    {
        using var context = _contextFactory();
        return context.GetOrCreateAppSettings().ActiveThemeKey;
    }

    private IPlatformSettings? _subscribedPlatformSettings;
    private EventHandler<PlatformColorValues>? _followSystemHandler;
    private DispatcherTimer? _scheduledCheckTimer;

    /// <summary>
    /// Wires up whichever <see cref="Data.Entities.ThemeAutoMode"/> is currently persisted - called
    /// once at startup (mirrors <see cref="ApplyPersistedSettings"/>'s own "called once, App.axaml.cs"
    /// convention) and again whenever the user changes the mode live via <see cref="SetThemeAutoMode"/>.
    /// Idempotent: always tears down any existing subscription/timer first.
    /// </summary>
    public void InitializeAutoMode()
    {
        TeardownAutoMode();

        using var context = _contextFactory();
        var mode = context.GetOrCreateAppSettings().ThemeAutoMode;

        switch (mode)
        {
            case Data.Entities.ThemeAutoMode.FollowSystem:
                SubscribeFollowSystem();
                break;
            case Data.Entities.ThemeAutoMode.Scheduled:
                StartScheduledCheck();
                break;
        }
    }

    private void TeardownAutoMode()
    {
        if (_subscribedPlatformSettings is not null && _followSystemHandler is not null)
        {
            _subscribedPlatformSettings.ColorValuesChanged -= _followSystemHandler;
        }
        _subscribedPlatformSettings = null;
        _followSystemHandler = null;

        _scheduledCheckTimer?.Stop();
        _scheduledCheckTimer = null;
    }

    /// <summary>
    /// Subscribes to Avalonia's own <see cref="IPlatformSettings.ColorValuesChanged"/> directly -
    /// not a second, redundant raw Win32 <c>SystemEvents.UserPreferenceChanged</c> hook (considered
    /// and rejected in review: the event needed already exists in Avalonia). Safe specifically
    /// because the 2026-09-10 freeze's cause was <c>FluentAvaloniaTheme</c>'s own handler rewriting
    /// <c>HighContrast</c> resources synchronously mid-<c>Popup.Open()</c> - not the event itself
    /// (see <see cref="FluentAvaloniaWorkarounds"/>, which permanently detaches only that specific
    /// handler, unconditionally, never re-subscribing). A second, different subscriber here is fine
    /// as long as it doesn't repeat that exact shape - which is exactly why the actual resource
    /// mutation below is deferred a dispatcher tick rather than run synchronously inline.
    /// </summary>
    private void SubscribeFollowSystem()
    {
        if (Application.Current?.PlatformSettings is not { } settings)
        {
            return;
        }

        _followSystemHandler = (_, e) => OnPlatformColorValuesChanged(e);
        settings.ColorValuesChanged += _followSystemHandler;
        _subscribedPlatformSettings = settings;

        // Apply immediately too, not just on the next OS notification - turning FollowSystem on
        // should reflect the current OS preference right away, not wait for it to next change.
        ApplyFollowSystemVariant(settings.GetColorValues().ThemeVariant);
    }

    private void OnPlatformColorValuesChanged(PlatformColorValues e)
    {
        // Deferred one dispatcher tick - see SubscribeFollowSystem's doc comment. ApplyTheme writes
        // ~20 Application.Resources entries; running that synchronously inside this callback risks
        // the same "resource-dictionary mutation during Popup.Open()" shape as the original freeze
        // if this event happens to fire while a popup is mid-open (a system dark-mode flip isn't
        // synchronized with what the user's cursor is doing).
        Dispatcher.UIThread.Post(() => ApplyFollowSystemVariant(e.ThemeVariant));
    }

    private void ApplyFollowSystemVariant(PlatformThemeVariant variant)
    {
        using var context = _contextFactory();
        var settings = context.GetOrCreateAppSettings();
        if (settings.ThemeAutoMode != Data.Entities.ThemeAutoMode.FollowSystem)
        {
            return; // mode changed since this was scheduled/the event fired
        }

        string? key = AutoSwitchTarget(settings, wantLight: variant == PlatformThemeVariant.Light);
        if (key is not null)
        {
            ApplyTheme(key);
        }
    }

    /// <summary>Minutes-scale periodic check, not per-frame - unaffected by the FollowSystem freeze-bug analysis above, no event subscription involved.</summary>
    private void StartScheduledCheck()
    {
        _scheduledCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _scheduledCheckTimer.Tick += (_, _) => CheckScheduledSwitch();
        _scheduledCheckTimer.Start();
        CheckScheduledSwitch(); // don't wait up to 5 minutes for the first check
    }

    /// <summary>
    /// Wraps at midnight via the standard two-hour-on-a-24h-clock comparison: when
    /// <c>darkHour &lt;= lightHour</c> the dark period is the contiguous span between them; otherwise
    /// (the common case - default 20/7) it's everything from <c>darkHour</c> to midnight plus
    /// midnight to <c>lightHour</c>. Also reuses this same tick to flip <see cref="Data.Entities.AppSettings.TrueBlackDark"/>
    /// at <see cref="Data.Entities.AppSettings.TrueBlackAutoHour"/> - one scheduling mechanism
    /// driving two settings, per the design doc's own "not a second scheduler" decision.
    /// </summary>
    private void CheckScheduledSwitch()
    {
        using var context = _contextFactory();
        var settings = context.GetOrCreateAppSettings();
        if (settings.ThemeAutoMode != Data.Entities.ThemeAutoMode.Scheduled)
        {
            return;
        }

        int hour = _now().Hour;
        bool isDarkPeriod = IsDarkPeriod(hour, settings.ThemeScheduledDarkHour, settings.ThemeScheduledLightHour);

        string? targetKey = AutoSwitchTarget(settings, wantLight: !isDarkPeriod);
        if (targetKey is not null && targetKey != settings.ActiveThemeKey)
        {
            ScheduledThemeCrossfadeRequested?.Invoke();
            ApplyTheme(targetKey);
        }

        if (settings.TrueBlackAutoHour == hour && !settings.TrueBlackDark)
        {
            SetTrueBlackDark(true);
        }
    }

    /// <summary>Whether <paramref name="hour"/> falls in the scheduled dark period. Shared with the startup splash, which must make the same pick.</summary>
    public static bool IsDarkPeriod(int hour, int darkHour, int lightHour) => darkHour <= lightHour
        ? hour >= darkHour && hour < lightHour
        : hour >= darkHour || hour < lightHour;

    /// <summary>Persists the mode and immediately re-wires the live watcher (§ InitializeAutoMode) - called from Preferences' Appearance section when the user changes the Auto mode selector.</summary>
    public void SetThemeAutoMode(Data.Entities.ThemeAutoMode mode)
    {
        using (var context = _contextFactory())
        {
            context.GetOrCreateAppSettings().ThemeAutoMode = mode;
            context.SaveChanges();
        }

        InitializeAutoMode();
        SaveSplashSnapshot();
    }

    public Data.Entities.ThemeAutoMode GetThemeAutoMode()
    {
        using var context = _contextFactory();
        return context.GetOrCreateAppSettings().ThemeAutoMode;
    }

    public (int DarkHour, int LightHour) GetThemeScheduleHours()
    {
        using var context = _contextFactory();
        var settings = context.GetOrCreateAppSettings();
        return (settings.ThemeScheduledDarkHour, settings.ThemeScheduledLightHour);
    }

    /// <summary>Persists both schedule hours and immediately re-checks (a changed hour should be reflected right away, not wait up to 5 minutes for the next tick).</summary>
    public void SetThemeScheduleHours(int darkHour, int lightHour)
    {
        using (var context = _contextFactory())
        {
            var settings = context.GetOrCreateAppSettings();
            settings.ThemeScheduledDarkHour = Math.Clamp(darkHour, 0, 23);
            settings.ThemeScheduledLightHour = Math.Clamp(lightHour, 0, 23);
            context.SaveChanges();
        }

        CheckScheduledSwitch();
        SaveSplashSnapshot();
    }

    public int? GetTrueBlackAutoHour()
    {
        using var context = _contextFactory();
        return context.GetOrCreateAppSettings().TrueBlackAutoHour;
    }

    public void SetTrueBlackAutoHour(int? hour)
    {
        using var context = _contextFactory();
        context.GetOrCreateAppSettings().TrueBlackAutoHour = hour is null ? null : Math.Clamp(hour.Value, 0, 23);
        context.SaveChanges();
    }

    public bool GetTrueBlackDark()
    {
        using var context = _contextFactory();
        return context.GetOrCreateAppSettings().TrueBlackDark;
    }

    /// <summary>
    /// Raised after <see cref="SetMatrixRainEnabled"/> persists a change - <c>MainViewModel</c>
    /// subscribes to show/hide the Matrix rain overlay live. Separate from <see cref="ThemeApplied"/>
    /// on purpose: nothing about the theme's resources changed, so consumers that re-render on a
    /// theme apply (Home's cover wall, icon caches) shouldn't re-run for a rain toggle.
    /// </summary>
    public event Action? MatrixRainEnabledChanged;

    public bool GetMatrixRainEnabled()
    {
        using var context = _contextFactory();
        return context.GetOrCreateAppSettings().MatrixRainEnabled;
    }

    public void SetMatrixRainEnabled(bool enabled)
    {
        using (var context = _contextFactory())
        {
            var settings = context.GetOrCreateAppSettings();
            settings.MatrixRainEnabled = enabled;
            context.SaveChanges();
        }

        MatrixRainEnabledChanged?.Invoke();
    }

    public string? GetAccentOverrideHex()
    {
        using var context = _contextFactory();
        return context.GetOrCreateAppSettings().AccentOverrideHex;
    }

    /// <summary>The inside counterpart of <see cref="BuildGlowRing"/>: same colour and tier, drawn within the control's own edge.</summary>
    public static BoxShadows BuildGlowRingInset(Color glowColor, int tier) =>
        NormalizeGlowTier(tier) switch
        {
            GlowTierOff => default,
            GlowTierSubtle => new BoxShadows(new BoxShadow { Spread = 1.5, IsInset = true, Color = Color.FromArgb(0x66, glowColor.R, glowColor.G, glowColor.B) }),
            GlowTierVivid => new BoxShadows(new BoxShadow { Spread = 2, IsInset = true, Color = Color.FromArgb(0xE6, glowColor.R, glowColor.G, glowColor.B) }),
            _ => new BoxShadows(new BoxShadow { Spread = 2, IsInset = true, Color = Color.FromArgb(0x99, glowColor.R, glowColor.G, glowColor.B) }),
        };

    /// <summary>Glow-intensity tiers (docs/superpowers/specs/2026-09-21-cosmetics-pitch-design.md #3).
    /// <see cref="GlowTierNormal"/> reproduces the ring exactly as it was before tiers existed.</summary>
    public const int GlowTierOff = 0;
    public const int GlowTierSubtle = 1;
    public const int GlowTierNormal = 2;
    public const int GlowTierVivid = 3;

    private static int _glowTier = GlowTierNormal;
    private static Color? _glowColor;

    public static int NormalizeGlowTier(int tier) => tier is >= GlowTierOff and <= GlowTierVivid ? tier : GlowTierNormal;

    /// <summary>The <c>PbGlowRing</c> value for a tier: Off is empty (no ring), Subtle a thinner fainter ring,
    /// Normal the original 4px ring at 0x99 alpha, Vivid a stronger ring plus a soft halo.</summary>
    public static BoxShadows BuildGlowRing(Color glowColor, int tier)
    {
        static BoxShadow Ring(Color c, byte alpha, double spread, double blur = 0) => new()
        {
            OffsetX = 0,
            OffsetY = 0,
            Blur = blur,
            Spread = spread,
            Color = Color.FromArgb(alpha, c.R, c.G, c.B),
        };

        return NormalizeGlowTier(tier) switch
        {
            GlowTierOff => default,
            GlowTierSubtle => new BoxShadows(Ring(glowColor, 0x66, 2)),
            GlowTierVivid => new BoxShadows(Ring(glowColor, 0xE6, 4), new[] { Ring(glowColor, 0x80, 0, 12) }),
            _ => new BoxShadows(Ring(glowColor, 0x99, 4)),
        };
    }

    /// <summary>Writes the density preset's spacing tokens into the live resources (docs/superpowers/specs/2026-09-21-cosmetics-pitch-2-design.md #17).</summary>
    internal static void ApplyDensityResources(int preset)
    {
        if (Application.Current is null)
        {
            return;
        }

        var values = DensityPresets.For(preset);
        var resources = Application.Current.Resources;
        resources["PbListRowPadding"] = values.ListRowPadding;
        resources["PbDetailsRowPadding"] = values.DetailsRowPadding;
        resources["PbSidebarItemPadding"] = values.SidebarItemPadding;
    }

    /// <summary>Returns the persisted density preset (0 Compact, 1 Comfortable, 2 Spacious).</summary>
    public int GetDensityPreset()
    {
        using var context = _contextFactory();
        return DensityPresets.Normalize(context.GetOrCreateAppSettings().DensityPreset);
    }

    /// <summary>Applies the density preset live and persists it.</summary>
    public void ApplyDensity(int preset)
    {
        preset = DensityPresets.Normalize(preset);
        ApplyDensityResources(preset);

        using var context = _contextFactory();
        var settings = context.GetOrCreateAppSettings();
        settings.DensityPreset = preset;
        context.SaveChanges();
    }

    /// <summary>Returns the currently persisted glow tier (0 Off .. 3 Vivid).</summary>
    public int GetGlowTier()
    {
        using var context = _contextFactory();
        return NormalizeGlowTier(context.GetOrCreateAppSettings().GlowTier);
    }

    /// <summary>Applies the glow tier live (rebuilds <c>PbGlowRing</c> from the active glow color) and persists it.</summary>
    public void ApplyGlowTier(int tier)
    {
        _glowTier = NormalizeGlowTier(tier);
        if (_glowColor is { } color && Application.Current is not null)
        {
            Application.Current.Resources["PbGlowRing"] = BuildGlowRing(color, _glowTier);
            Application.Current.Resources["PbGlowRingInset"] = BuildGlowRingInset(color, _glowTier);
        }

        using var context = _contextFactory();
        var settings = context.GetOrCreateAppSettings();
        settings.GlowTier = _glowTier;
        context.SaveChanges();
    }

    /// <summary>The built-in era colours (docs/superpowers/specs/2026-09-28-continuity-screen-redesign-design.md, "Colour tokens"), named
    /// after the ages - platinum grey, gold, silver, bronze - in a set for dark themes and a deeper set that keeps contrast on light ones.
    /// Modern has no fixed default: it follows the accent.</summary>
    private static readonly (string Key, string Dark, string Light)[] s_eraDefaults =
    {
        ("Platinum", "#9AA5B1", "#5F6B78"),
        ("Golden", "#D4A437", "#9A7310"),
        ("Silver", "#B8C0CC", "#6E7886"),
        ("Bronze", "#C07A45", "#96542A"),
    };

    /// <summary>
    /// The five era colours for a theme, keyed Platinum…Modern: the theme's optional <c>era…</c> value when set and valid, else the
    /// default for its mode; Modern falls back to <paramref name="accentOverride"/> (a user accent override) or the theme's accent.
    /// </summary>
    internal static IReadOnlyDictionary<string, Color> EraColors(ThemeDefinition theme, Color? accentOverride = null)
    {
        bool light = string.Equals(theme.Mode, "Light", StringComparison.OrdinalIgnoreCase);
        var colors = theme.Colors;
        var result = new Dictionary<string, Color>();
        foreach (var (key, dark, lightHex) in s_eraDefaults)
        {
            string? custom = key switch
            {
                "Platinum" => colors.EraPlatinum,
                "Golden" => colors.EraGolden,
                "Silver" => colors.EraSilver,
                _ => colors.EraBronze,
            };
            result[key] = custom is not null && Color.TryParse(custom, out var parsed) ? parsed : Color.Parse(light ? lightHex : dark);
        }

        result["Modern"] = colors.EraModern is not null && Color.TryParse(colors.EraModern, out var modern) ? modern
            : accentOverride ?? (Color.TryParse(colors.Accent, out var accent) ? accent : Color.Parse("#C9803F"));
        return result;
    }

    /// <summary>WCAG contrast ratio; see <see cref="Theming.ThemeColorMath.ContrastRatio"/> (moved there so the resolver can use it without an application).</summary>
    internal static double ContrastRatio(Color a, Color b) => Theming.ThemeColorMath.ContrastRatio(a, b);

    /// <summary>
    /// Adjusts <paramref name="fg"/> toward whichever direction increases contrast against <paramref name="bg"/> - darker on a light background,
    /// lighter on a dark one - until it reaches <paramref name="targetRatio"/>. See <see cref="Theming.ThemeColorMath.AdjustForContrast"/>.
    /// </summary>
    internal static Color AdjustForContrast(Color fg, Color bg, double targetRatio) => Theming.ThemeColorMath.AdjustForContrast(fg, bg, targetRatio);

    /// <summary>Returns the currently persisted font family override, or null (app default).</summary>
    public string? GetSelectedFontFamily()
    {
        using var context = _contextFactory();
        return context.GetOrCreateAppSettings().SelectedFontFamily;
    }

    /// <summary>
    /// Applies a font override live (null/"System Default" clears it) and persists the choice.
    /// Clearing the override (null) doesn't necessarily mean the hardcoded default font - it falls
    /// through to the active theme's own <see cref="ThemeDefinition.DefaultFontFamily"/> if it has
    /// one (§ Extended scope's "Per-theme default font"), same precedence <see cref="ApplyTheme"/>
    /// already applies.
    /// </summary>
    public void ApplyFont(string? fontFamilyName)
    {
        string? normalized = fontFamilyName is null or "System Default" ? null : fontFamilyName;

        using var context = _contextFactory();
        var settings = context.GetOrCreateAppSettings();
        var theme = LoadTheme(settings.ActiveThemeKey);
        ApplyFontResource(normalized, theme.DefaultFontFamily);

        settings.SelectedFontFamily = normalized;
        context.SaveChanges();
    }

    /// <summary>
    /// The new bundled-default body font (docs/superpowers/specs/2026-08-24-design-language-
    /// foundation-design.md Typography section) - what "no override, no theme default either"
    /// resolves to. Deliberately a live default value, not an absent key, unlike the pre-this-phase
    /// behavior: ApplyFontResource used to Remove("PbFontFamily") when there was no override,
    /// relying on Avalonia's graceful fallback to the FluentTheme/OS default for an unresolved
    /// DynamicResource. Bebas Neue (PbDisplayFontFamily, App.axaml) is a separate, non-overridable
    /// resource for hero/heading text only - it never goes through this override mechanism.
    /// </summary>
    private static readonly FontFamily DefaultFontFamily =
        new("avares://Paperbunkr.App/Assets/Fonts/#Source Serif 4, Georgia, serif");

    /// <summary>
    /// Precedence (docs/superpowers/specs/2026-09-16-theme-system-design.md § Extended scope):
    /// <paramref name="explicitOverride"/> (the user's own global choice) wins if set; else
    /// <paramref name="themeDefaultFontFamily"/> (the active theme's own suggestion, e.g. Matrix's
    /// monospace); else the hardcoded <see cref="DefaultFontFamily"/>. Purely additive - a theme
    /// that omits <c>defaultFontFamily</c> behaves exactly as before this field existed.
    /// </summary>
    private static void ApplyFontResource(string? explicitOverride, string? themeDefaultFontFamily)
    {
        var resources = Application.Current!.Resources;
        string? resolved = explicitOverride ?? themeDefaultFontFamily;
        resources["PbFontFamily"] = resolved is null ? DefaultFontFamily : new FontFamily(resolved);
    }

    /// <summary>Returns the persisted "Splash ambient motion" preference (docs/superpowers/specs/2026-09-21-cosmetics-pitch-design.md #6).</summary>
    public bool GetSplashAmbientMotion()
    {
        using var context = _contextFactory();
        return context.GetOrCreateAppSettings().SplashAmbientMotion;
    }

    /// <summary>Returns the currently persisted reduced-motion preference.</summary>
    public bool GetReducedMotion()
    {
        return Theming.Motion.MotionPolicy.IsReducedMotion(GetMotionPreference());
    }

    public MotionPreferenceMode GetMotionPreference()
    {
        bool platformPreferenceChanged = Theming.Motion.MotionPolicy.RefreshSystemPreference();
        using var context = _contextFactory();
        var preference = context.GetOrCreateAppSettings().MotionPreference;
        Theming.Motion.MotionPolicy.SetPreference(preference);
        Theming.Motion.MotionPolicy.SetEffectQuality(GetEffectQuality());
        if (platformPreferenceChanged)
        {
            ApplyMotionResources(preference);
            Theming.Motion.ThemeMotionRuntime.SetPreference(preference);
        }

        return preference;
    }

    public bool IsSystemMotionPreferenceAvailable
    {
        get
        {
            Theming.Motion.MotionPolicy.RefreshSystemPreference();
            return Theming.Motion.MotionPolicy.IsSystemPreferenceAvailable;
        }
    }

    public void ApplyMotionPreference(MotionPreferenceMode preference)
    {
        Theming.Motion.MotionPolicy.RefreshSystemPreference();
        ApplyMotionResources(preference);
        Theming.Motion.ThemeMotionRuntime.SetPreference(preference);

        using var context = _contextFactory();
        var settings = context.GetOrCreateAppSettings();
        settings.MotionPreference = preference;
        settings.ReducedMotion = preference is MotionPreferenceMode.Reduced or MotionPreferenceMode.Off;
        context.SaveChanges();
    }

    /// <summary>
    /// Legacy on/off entry point: true is Reduced, false is Full. The tokens are rewritten live, the same "overwrite the resource"
    /// approach ApplyFontResource uses.
    /// </summary>
    public void ApplyReducedMotion(bool enabled)
    {
        ApplyMotionPreference(enabled ? MotionPreferenceMode.Reduced : MotionPreferenceMode.Full);
    }

    private void ApplyMotionResources(MotionPreferenceMode preference)
    {
        Theming.Motion.MotionPolicy.SetPreference(preference);
        Theming.Motion.MotionPolicy.SetEffectQuality(GetEffectQuality());
        if (Application.Current is not { } app)
        {
            return;
        }

        WriteMotionTokens(app.Resources);
        ApplyMotionWindowClass();
    }

    /// <summary>
    /// Writes the role tokens (docs/superpowers/specs/2026-10-20-motion-system-v2-foundation-overlays-design.md §3.4) from
    /// <see cref="Theming.Motion.MotionPolicy.Resolve"/>, plus the four legacy keys as aliases of their role equivalents, so every
    /// existing <c>{DynamicResource PbMotion*}</c> follows the same policy. Reduced keeps the fade tokens short and zeroes the move
    /// tokens; Off zeroes everything.
    /// </summary>
    internal static void WriteMotionTokens(Avalonia.Controls.IResourceDictionary resources)
    {
        var acknowledge = Theming.Motion.MotionPolicy.Resolve(Theming.Motion.MotionRole.Acknowledge);
        var hover = Theming.Motion.MotionPolicy.Resolve(Theming.Motion.MotionRole.Hover);
        var reveal = Theming.Motion.MotionPolicy.Resolve(Theming.Motion.MotionRole.Reveal);
        var replace = Theming.Motion.MotionPolicy.Resolve(Theming.Motion.MotionRole.Replace);
        var travel = Theming.Motion.MotionPolicy.Resolve(Theming.Motion.MotionRole.Travel);
        var ambient = Theming.Motion.MotionPolicy.Resolve(Theming.Motion.MotionRole.Ambient);
        var flight = Theming.Motion.MotionPolicy.FlightDuration();

        resources["PbMotionAcknowledge"] = acknowledge.Fade;
        resources["PbMotionAcknowledgeMove"] = acknowledge.Move;
        resources["PbMotionHoverFade"] = hover.Fade;
        resources["PbMotionHoverMove"] = hover.Move;
        resources["PbMotionRevealFade"] = reveal.Fade;
        resources["PbMotionRevealMove"] = reveal.Move;
        resources["PbMotionRevealExit"] = reveal.Exit;
        resources["PbMotionReplace"] = replace.Fade;
        resources["PbMotionTravelFade"] = travel.Fade;
        resources["PbMotionTravelMove"] = travel.Move;
        resources["PbMotionFlight"] = flight;
        resources["PbMotionAmbient"] = ambient.Fade;

        // Legacy aliases (spec §3.4): Fast → Hover fade, Standard → Reveal fade, Large → Flight, Slow → Ambient.
        resources["PbMotionFast"] = hover.Fade;
        resources["PbMotionStandard"] = reveal.Fade;
        resources["PbMotionLarge"] = flight;
        resources["PbMotionSlow"] = ambient.Fade;
    }

    /// <summary>Class on every app window while motion is Reduced or Off (spec §4.3) - the only thing styles may key off, to drop the
    /// transforms that are *held* while a state is on (press scale, hover lift).</summary>
    public const string ReducedMotionWindowClass = "motionReduced";

    private static bool s_windowClassHookInstalled;

    /// <summary>Applies <see cref="ReducedMotionWindowClass"/> to every open window, and (once) to every window that loads later; the first
    /// call also installs the app-wide popup entrance (<see cref="Controls.PopupMotion"/>).</summary>
    internal static void ApplyMotionWindowClass()
    {
        if (!s_windowClassHookInstalled)
        {
            s_windowClassHookInstalled = true;
            Avalonia.Controls.Control.LoadedEvent.AddClassHandler<Avalonia.Controls.Window>((window, _) => SetMotionWindowClass(window));
            Controls.PopupMotion.Install();
            Controls.TabIndicator.Install();
        }

        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var window in desktop.Windows)
            {
                SetMotionWindowClass(window);
            }
        }
    }

    internal static void SetMotionWindowClass(Avalonia.Controls.Window window) =>
        window.Classes.Set(ReducedMotionWindowClass, Theming.Motion.MotionPolicy.IsReducedMotion(Theming.Motion.MotionPolicy.Preference));

    /// <summary>Cross-platform replacement for GDI+'s InstalledFontCollection - prepends "System Default" (no override).</summary>
    public IReadOnlyList<string> GetInstalledFontFamilies()
    {
        var families = new List<string> { "System Default" };
        families.AddRange(SKFontManager.Default.FontFamilies.Distinct().OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
        return families;
    }

    public void OpenSkinsFolder()
    {
        Directory.CreateDirectory(ThemePaths.InstalledDirectory);
        try
        {
            Process.Start(new ProcessStartInfo { FileName = ThemePaths.InstalledDirectory, UseShellExecute = true });
        }
        catch
        {
            // No shell/file-manager available (e.g. some CI/headless environments) - nothing more we can do.
        }
    }

}
