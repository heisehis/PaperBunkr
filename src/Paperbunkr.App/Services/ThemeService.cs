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
public class ThemeService
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

    /// <summary>
    /// Embedded built-in themes (docs/superpowers/specs/2026-09-07-preferences-tile-hub-redesign-
    /// design.md §3) - <see cref="DefaultThemeKey"/> used to be the only one read from an
    /// <c>avares://</c> resource here; everything else fell through to <c>ThemePaths.ExtractedDirectory</c>
    /// (user-installed themes only). That meant "windows_11" existed as a real
    /// <c>Assets/Skins/windows_11/theme.json</c> file on disk but was never actually reachable from
    /// <see cref="GetAvailableThemes"/> - found and fixed as part of adding the 3 new built-in skins,
    /// since this method needed generalizing to a list anyway.
    /// </summary>
    private static readonly string[] BuiltInThemeKeys =
    {
        DefaultThemeKey, "windows_11", "cool_technical", "vibrant_pop", "vintage_paperback",
        "daylight", "overcast", "maximum_contrast", "colorblind_safe", "matrix",
    };

    private static string BuiltInThemeAssetRoot(string key) => $"avares://Paperbunkr.App/Assets/Skins/{key}/";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly Dictionary<string, Bitmap> _iconCache = new();
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly Func<DateTimeOffset> _now;

    public ThemeService()
        : this(PaperbunkrDb.CreateContext)
    {
    }

    /// <summary>Test-only seam - production always uses the default ctor (the real per-user database).</summary>
    internal ThemeService(Func<PaperbunkrDbContext> contextFactory, Func<DateTimeOffset>? now = null)
    {
        _contextFactory = contextFactory;
        _now = now ?? (() => DateTimeOffset.Now);
    }

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

    public IReadOnlyList<ThemeSummary> GetAvailableThemes()
    {
        using var context = _contextFactory();
        string activeKey = context.GetOrCreateAppSettings().ActiveThemeKey;

        var summaries = new List<ThemeSummary>();

        foreach (string key in BuiltInThemeKeys)
        {
            var theme = TryLoadTheme(key);
            if (theme is not null)
            {
                summaries.Add(SummaryFrom(key, theme, activeKey));
            }
        }

        if (Directory.Exists(ThemePaths.ExtractedDirectory))
        {
            foreach (string dir in Directory.GetDirectories(ThemePaths.ExtractedDirectory).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string key = Path.GetFileName(dir);
                if (BuiltInThemeKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                {
                    // An installed theme can't shadow a built-in key - the built-in embedded copy
                    // above already won.
                    continue;
                }

                var theme = TryLoadTheme(key);
                if (theme is not null)
                {
                    summaries.Add(SummaryFrom(key, theme, activeKey));
                }
            }
        }

        return summaries;
    }

    /// <summary>Preview-card colors (docs/superpowers/specs/2026-09-07-appearance-redesign-design.md) - parsed once here alongside the theme.json read this method already does for the theme's Name, rather than re-parsing hex on every View bind.</summary>
    private static ThemeSummary SummaryFrom(string key, ThemeDefinition theme, string activeKey) => new()
    {
        Key = key,
        Name = theme.Name,
        IsActive = activeKey == key,
        Mode = theme.Mode,
        BackgroundBrush = Brush(theme.Colors.Bg),
        ChromeBrush = Brush(theme.Colors.Chrome),
        AccentBrush = Brush(theme.Colors.Accent),
        SurfaceBrush = Brush(theme.Colors.Surface3),
        TextMutedBrush = Brush(theme.Colors.TextMuted),
    };

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    /// <summary>Parses the given theme's <c>theme.json</c>. Throws if the theme doesn't exist or is invalid.</summary>
    public ThemeDefinition LoadTheme(string key)
    {
        return TryLoadTheme(key) ?? throw new InvalidOperationException($"Theme '{key}' was not found or its theme.json is invalid.");
    }

    private static ThemeDefinition? TryLoadTheme(string key)
    {
        try
        {
            string json = BuiltInThemeKeys.Contains(key, StringComparer.OrdinalIgnoreCase)
                ? ReadEmbeddedText(BuiltInThemeAssetRoot(key) + "theme.json")
                : File.ReadAllText(Path.Combine(ThemePaths.ExtractedDirectory, key, "theme.json"));

            return JsonSerializer.Deserialize<ThemeDefinition>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string ReadEmbeddedText(string assetUri)
    {
        using var stream = AssetLoader.Open(new Uri(assetUri));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Applies <paramref name="key"/>'s colors/spacing/radius live to every screen and persists it as the active theme.</summary>
    public void ApplyTheme(string key)
    {
        var theme = LoadTheme(key);
        ApplyThemeResources(theme);

        using var context = _contextFactory();
        var settings = context.GetOrCreateAppSettings();
        settings.ActiveThemeKey = key;

        // Last-picked pair for Auto mode (§ Extended scope) - updated on every manual apply,
        // regardless of whether Auto mode is currently on, so it always reflects the user's most
        // recent real choice for that mode.
        bool isLight = string.Equals(theme.Mode, "Light", StringComparison.OrdinalIgnoreCase);
        if (isLight)
        {
            settings.LastLightThemeKey = key;
        }
        else
        {
            settings.LastDarkThemeKey = key;
        }

        context.SaveChanges();

        ApplyFontResource(settings.SelectedFontFamily, theme.DefaultFontFamily);
        ApplyTrueBlackIfNeeded(theme, settings.TrueBlackDark);
        ApplyAccentOverrideIfSet(settings.AccentOverrideHex, theme);

        _iconCache.Clear();
        ThemeApplied?.Invoke();
        WindowBackdropRequested?.Invoke(theme.WindowBackdrop);
    }

    /// <summary>Re-applies whatever theme/font is already persisted in <see cref="Data.Entities.AppSettings"/> - called once on startup.</summary>
    public void ApplyPersistedSettings()
    {
        using var context = _contextFactory();
        var settings = context.GetOrCreateAppSettings();
        var theme = LoadTheme(settings.ActiveThemeKey);
        ApplyThemeResources(theme);
        ApplyFontResource(settings.SelectedFontFamily, theme.DefaultFontFamily);
        Application.Current!.Resources["PbMotionFast"] = settings.ReducedMotion ? TimeSpan.Zero : DefaultMotionFast;
        Application.Current!.Resources["PbMotionSlow"] = settings.ReducedMotion ? TimeSpan.Zero : DefaultMotionSlow;
        Application.Current!.Resources["PbMotionStandard"] = settings.ReducedMotion ? TimeSpan.Zero : DefaultMotionStandard;
        Application.Current!.Resources["PbMotionLarge"] = settings.ReducedMotion ? TimeSpan.Zero : DefaultMotionLarge;

        ApplyTrueBlackIfNeeded(theme, settings.TrueBlackDark);
        ApplyAccentOverrideIfSet(settings.AccentOverrideHex, theme);

        ThemeApplied?.Invoke();
    }

    /// <summary>
    /// Toggles true black live and persists it, restoring via a full <see cref="ApplyTheme"/>
    /// re-run on "off" rather than caching original values (docs/superpowers/specs/2026-09-16-
    /// theme-system-design.md § Variant switching + true black - "Toggling off" note) - reuses an
    /// existing, already-tested code path instead of adding a second one to keep in sync.
    /// </summary>
    public void SetTrueBlackDark(bool enabled)
    {
        using var context = _contextFactory();
        var settings = context.GetOrCreateAppSettings();
        settings.TrueBlackDark = enabled;
        context.SaveChanges();

        // Re-run the normal apply path - correct for both directions: "on" needs the override step
        // that follows a plain ApplyThemeResources anyway, "off" needs the real theme.json values
        // back, which only a fresh LoadTheme provides.
        var theme = LoadTheme(settings.ActiveThemeKey);
        ApplyThemeResources(theme);
        ApplyTrueBlackIfNeeded(theme, enabled);
        ApplyAccentOverrideIfSet(settings.AccentOverrideHex, theme);
    }

    /// <summary>
    /// Live-only true-black suspend/resume for the Reader screens (docs/superpowers/specs/
    /// 2026-09-16-theme-system-design.md § Reader auto-suspend) - never touches the persisted
    /// <see cref="Data.Entities.AppSettings.TrueBlackDark"/> value, only the live resources, so the
    /// user's actual preference survives a reading session unaltered. <paramref name="suspend"/>
    /// true = force the theme's real (non-#000000) values regardless of the persisted setting;
    /// false = re-apply true black if the persisted setting is still on.
    /// </summary>
    public void SetTrueBlackReaderSuspend(bool suspend)
    {
        using var context = _contextFactory();
        var settings = context.GetOrCreateAppSettings();
        var theme = LoadTheme(settings.ActiveThemeKey);
        ApplyThemeResources(theme);
        ApplyTrueBlackIfNeeded(theme, !suspend && settings.TrueBlackDark);
        ApplyAccentOverrideIfSet(settings.AccentOverrideHex, theme);
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

        string? key = variant == PlatformThemeVariant.Light ? settings.LastLightThemeKey : settings.LastDarkThemeKey;
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
        int darkHour = settings.ThemeScheduledDarkHour;
        int lightHour = settings.ThemeScheduledLightHour;
        bool isDarkPeriod = darkHour <= lightHour
            ? hour >= darkHour && hour < lightHour
            : hour >= darkHour || hour < lightHour;

        string? targetKey = isDarkPeriod ? settings.LastDarkThemeKey : settings.LastLightThemeKey;
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

    /// <summary>Persists the mode and immediately re-wires the live watcher (§ InitializeAutoMode) - called from Preferences' Appearance section when the user changes the Auto mode selector.</summary>
    public void SetThemeAutoMode(Data.Entities.ThemeAutoMode mode)
    {
        using (var context = _contextFactory())
        {
            context.GetOrCreateAppSettings().ThemeAutoMode = mode;
            context.SaveChanges();
        }

        InitializeAutoMode();
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

    /// <summary>Persists and re-applies live immediately - null/whitespace clears the override, reverting to the active theme's own accent.</summary>
    public void SetAccentOverrideHex(string? hex)
    {
        string? normalized = string.IsNullOrWhiteSpace(hex) ? null : hex;

        using var context = _contextFactory();
        var settings = context.GetOrCreateAppSettings();
        settings.AccentOverrideHex = normalized;
        context.SaveChanges();

        var theme = LoadTheme(settings.ActiveThemeKey);
        ApplyThemeResources(theme);
        ApplyTrueBlackIfNeeded(theme, settings.TrueBlackDark);
        ApplyAccentOverrideIfSet(normalized, theme);
    }

    /// <summary>
    /// Overwrites PbBg/PbChrome/PbSurface0-3 to #000000 when <paramref name="enabled"/> and the
    /// theme's mode is Dark - a global modifier, not per-theme variant entries, matching Mihon/
    /// Komikku's actual mechanism. No-op under a Light theme (including when Matrix's surfaces are
    /// already near-black - the visible change there is minimal/none, expected, not a bug).
    /// </summary>
    private static void ApplyTrueBlackIfNeeded(ThemeDefinition theme, bool enabled)
    {
        if (!enabled || !string.Equals(theme.Mode, "Dark", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var resources = Application.Current!.Resources;
        SetColorAndBrush(resources, "PbBg", "#000000");
        SetColorAndBrush(resources, "PbChrome", "#000000");
        SetColorAndBrush(resources, "PbSurface0", "#000000");
        SetColorAndBrush(resources, "PbSurface1", "#000000");
        SetColorAndBrush(resources, "PbSurface2", "#000000");
        SetColorAndBrush(resources, "PbSurface3", "#000000");
    }

    private static void ApplyThemeResources(ThemeDefinition theme)
    {
        var resources = Application.Current!.Resources;

        SetColorAndBrush(resources, "PbBg", theme.Colors.Bg);
        SetColorAndBrush(resources, "PbChrome", theme.Colors.Chrome);
        SetColorAndBrush(resources, "PbBorder", theme.Colors.Border);
        SetColorAndBrush(resources, "PbText", theme.Colors.Text);
        SetColorAndBrush(resources, "PbTextMuted", theme.Colors.TextMuted);
        SetColorAndBrush(resources, "PbTextFaint", theme.Colors.TextFaint);
        SetColorAndBrush(resources, "PbAccent", theme.Colors.Accent);
        SetColorAndBrush(resources, "PbAccentText", theme.Colors.AccentText);
        SetColorAndBrush(resources, "PbAccentSoft", theme.Colors.AccentSoft);
        SetColorAndBrush(resources, "PbBadge", theme.Colors.Badge);
        SetColorAndBrush(resources, "PbBadgeText", theme.Colors.BadgeText);
        SetColorAndBrush(resources, "PbSuccess", theme.Colors.Success);
        SetColorAndBrush(resources, "PbChartBlue", theme.Colors.ChartBlue);
        SetColorAndBrush(resources, "PbChartViolet", theme.Colors.ChartViolet);
        SetColorAndBrush(resources, "PbSurface0", theme.Colors.Surface0);
        SetColorAndBrush(resources, "PbSurface1", theme.Colors.Surface1);
        SetColorAndBrush(resources, "PbSurface2", theme.Colors.Surface2);
        SetColorAndBrush(resources, "PbSurface3", theme.Colors.Surface3);
        SetColorAndBrush(resources, "PbGlow", theme.Colors.Glow);
        ApplyGlowRing(resources, Color.Parse(theme.Colors.Glow));

        resources["PbHeroGradientStartColor"] = Color.Parse(theme.Colors.HeroGradientStart);
        resources["PbHeroGradientEndColor"] = Color.Parse(theme.Colors.HeroGradientEnd);

        ApplyAccentColor(theme.Colors.Accent);

        resources["PbSpacingUnit"] = theme.SpacingUnit;

        // CornerRadius, not double - see the matching comment on PbRadius in App.axaml for why.
        resources["PbRadius"] = new CornerRadius(theme.Radius);
        resources["PbRadiusSm"] = new CornerRadius(theme.RadiusSm);
        resources["PbRadiusLg"] = new CornerRadius(theme.RadiusLg);

        // Schema + resource only (§ Extended scope's "Scrollbar geometry tokens") - not consumed by
        // any ControlTheme yet, see ThemeDefinition.ScrollbarWidth's own doc comment for why. Only
        // written when the theme actually sets a value, so a theme that omits them leaves whatever
        // (currently nothing) was there before rather than resetting to some hardcoded number.
        if (theme.ScrollbarWidth is { } scrollbarWidth)
        {
            resources["PbScrollbarWidth"] = scrollbarWidth;
        }
        if (theme.ScrollbarThumbOpacity is { } scrollbarThumbOpacity)
        {
            resources["PbScrollbarThumbOpacity"] = scrollbarThumbOpacity;
        }

        ApplyThemeVariant(theme.Mode);
    }

    /// <summary>
    /// Drives <see cref="Application.RequestedThemeVariant"/> from the theme's own <c>mode</c>
    /// (docs/superpowers/specs/2026-09-16-theme-system-design.md § Variant switching) - previously
    /// hardcoded <c>RequestedThemeVariant="Dark"</c> in App.axaml regardless of which theme was
    /// active, which is why the pre-existing "windows_11" theme's native FluentAvalonia chrome
    /// (ComboBox popup, ScrollBar, unthemed CheckBox parts) never actually followed its own light
    /// <c>Pb*</c> colors before this. Matrix ships <c>mode: "Dark"</c> too - FluentAvalonia has no
    /// third "Matrix" variant, and none is needed since every native-chrome color still comes from
    /// the Dark theme dictionary while all <c>Pb*</c> tokens carry Matrix's own green palette.
    /// Case-insensitive compare, unknown/missing values fall back to Dark (matches
    /// <see cref="Models.ThemeDefinition.Mode"/>'s own default).
    /// </summary>
    private static void ApplyThemeVariant(string mode)
    {
        var variant = string.Equals(mode, "Light", StringComparison.OrdinalIgnoreCase)
            ? ThemeVariant.Light
            : ThemeVariant.Dark;

        // Real bug caught by tests: unlike the plain Application.Resources indexer writes elsewhere
        // in this file, RequestedThemeVariant is a StyledProperty whose setter enforces
        // Dispatcher.VerifyAccess() - it throws "calling thread cannot access this object" when
        // ApplyTheme runs on a thread the dispatcher doesn't recognize as its own. Production always
        // calls this from the real UI thread (CheckAccess() true), so the guard below only ever
        // matters for certain test-runner threads in the headless fixture. Marshaling via
        // Dispatcher.UIThread.Invoke was tried first and rejected - it deadlocked, because those
        // particular test threads aren't running a pumped dispatcher loop for Invoke to eventually
        // complete against. Skipping the set on those threads is safe: every test that actually
        // asserts on RequestedThemeVariant happens to run on the recognized thread already (verified
        // by running them), and every other Pb* resource write in this method is unaffected either
        // way, since only this one StyledProperty enforces thread ownership.
        if (Dispatcher.UIThread.CheckAccess())
        {
            Application.Current!.RequestedThemeVariant = variant;
        }
    }

    /// <summary>
    /// Pushes the theme's accent into FluentAvalonia's stock-control accent system
    /// (<see cref="FluentAvaloniaTheme.CustomAccentColor"/>), which seeds SystemAccentColor and its
    /// 6 tonal variants. This is the FluentAvalonia equivalent of the old
    /// <c>FluentTheme.Palettes</c> <c>Accent="{DynamicResource PbAccentColor}"</c> binding - it
    /// keeps a live theme switch recoloring stock controls (TextBox focus, ToggleSwitch, ScrollBar,
    /// selection), not just the app's own Pb*-token UI.
    /// </summary>
    private static void ApplyAccentColor(string accentHex)
    {
        if (Application.Current is null || !Color.TryParse(accentHex, out var accent))
        {
            return;
        }

        foreach (var style in Application.Current.Styles)
        {
            if (style is FluentAvaloniaTheme faTheme)
            {
                faTheme.CustomAccentColor = accent;
                return;
            }
        }
    }

    private static void SetColorAndBrush(IResourceDictionary resources, string keyPrefix, string hex)
    {
        var color = Color.Parse(hex);
        resources[$"{keyPrefix}Color"] = color;
        resources[$"{keyPrefix}Brush"] = new SolidColorBrush(color);
    }

    private static void SetColorAndBrush(IResourceDictionary resources, string keyPrefix, Color color)
    {
        resources[$"{keyPrefix}Color"] = color;
        resources[$"{keyPrefix}Brush"] = new SolidColorBrush(color);
    }

    /// <summary>
    /// Library/Books/Detail/Reader/Preferences card hover-focus ring - previously a hardcoded
    /// literal-color <c>BoxShadows</c> resource in <c>Primitives.axaml</c> (its own doc comment
    /// flagged this as "an accepted implementation-time limitation for this phase" back when
    /// "default" was the only real skin) that never reacted to a theme switch, unlike every other
    /// <c>Pb*</c> token. <c>BoxShadows</c> is parsed from a single string and can't embed a
    /// <c>DynamicResource</c> inside it, so this rebuilds the value in code instead - same fix
    /// shape as <see cref="ApplyThemeVariant"/> existing for the analogous
    /// <c>RequestedThemeVariant</c> string-can't-bind problem. Ring alpha (0x99) is intentionally
    /// higher than the ambient <c>PbGlow</c> token's own alpha (0x66, see <c>theme.json</c>'s
    /// <c>glow</c> field) - same hue, bumped opacity - matching the superseded static resource's own
    /// tuning note ("still too faint to read clearly against a card's own drop shadow").
    /// </summary>
    private static void ApplyGlowRing(IResourceDictionary resources, Color glowColor)
    {
        resources["PbGlowRing"] = new BoxShadows(new BoxShadow
        {
            OffsetX = 0,
            OffsetY = 0,
            Blur = 0,
            Spread = 4,
            Color = Color.FromArgb(0x99, glowColor.R, glowColor.G, glowColor.B),
        });
    }

    /// <summary>
    /// Global accent override (docs/superpowers/specs/2026-09-16-theme-system-design.md § Custom
    /// accent color picker) - no-op when unset. Overwrites PbAccent/PbAccentText/PbAccentSoft/PbGlow
    /// on top of whatever <see cref="ApplyThemeResources"/> already wrote, and re-pushes the
    /// overridden hex into FluentAvalonia's accent system too, so native chrome matches.
    /// </summary>
    private static void ApplyAccentOverrideIfSet(string? accentHex, ThemeDefinition theme)
    {
        if (string.IsNullOrWhiteSpace(accentHex) || !Color.TryParse(accentHex, out var accent)
            || !Color.TryParse(theme.Colors.Bg, out var bg))
        {
            return;
        }

        var accentText = AdjustForContrast(accent, bg, targetRatio: 4.5);
        ApplyAccentColor(accentHex);

        var resources = Application.Current!.Resources;
        SetColorAndBrush(resources, "PbAccent", accent);
        SetColorAndBrush(resources, "PbAccentText", accentText);
        SetColorAndBrush(resources, "PbAccentSoft", Color.FromArgb(0x29, accent.R, accent.G, accent.B));
        var glow = Color.FromArgb(0x66, accentText.R, accentText.G, accentText.B);
        SetColorAndBrush(resources, "PbGlow", glow);
        ApplyGlowRing(resources, glow);
    }

    private static double RelativeLuminance(Color c)
    {
        double Lin(byte channel)
        {
            double v = channel / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Lin(c.R)) + (0.7152 * Lin(c.G)) + (0.0722 * Lin(c.B));
    }

    private static double ContrastRatio(Color a, Color b)
    {
        double l1 = RelativeLuminance(a);
        double l2 = RelativeLuminance(b);
        if (l2 > l1)
        {
            (l1, l2) = (l2, l1);
        }

        return (l1 + 0.05) / (l2 + 0.05);
    }

    /// <summary>
    /// Adjusts <paramref name="fg"/>'s lightness toward whichever direction increases contrast
    /// against <paramref name="bg"/>, iterating to <paramref name="targetRatio"/> - deliberately
    /// NOT a fixed "always darken" rule. A first version of this used that fixed rule and was wrong:
    /// darkening only helps when <paramref name="bg"/> is light; against a dark <paramref name="bg"/>
    /// it makes contrast worse, which is exactly the case whenever the active theme is Dark (caught
    /// in a review round, since <see cref="Data.Entities.AppSettings.AccentOverrideHex"/> is global
    /// and has to stay legible against whichever theme is active, not just a Light one). Direction is
    /// decided from <paramref name="bg"/>'s own luminance, never <paramref name="fg"/>'s.
    /// </summary>
    private static Color AdjustForContrast(Color fg, Color bg, double targetRatio)
    {
        if (ContrastRatio(fg, bg) >= targetRatio)
        {
            return fg;
        }

        bool bgIsLight = RelativeLuminance(bg) > 0.5;
        Color current = fg;
        for (int i = 0; i < 20; i++)
        {
            current = bgIsLight
                ? Color.FromRgb((byte)(current.R * 0.9), (byte)(current.G * 0.9), (byte)(current.B * 0.9))
                : Color.FromRgb(
                    (byte)Math.Min(255, current.R + ((255 - current.R) * 0.12)),
                    (byte)Math.Min(255, current.G + ((255 - current.G) * 0.12)),
                    (byte)Math.Min(255, current.B + ((255 - current.B) * 0.12)));

            if (ContrastRatio(current, bg) >= targetRatio)
            {
                return current;
            }
        }

        // 20 iterations weren't enough (a picked hue that simply can't reach the target without
        // going fully to an extreme) - clamp to the safest fallback in the correct direction.
        return bgIsLight ? Colors.Black : Colors.White;
    }

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

    /// <summary>Returns the currently persisted reduced-motion preference.</summary>
    public bool GetReducedMotion()
    {
        using var context = _contextFactory();
        return context.GetOrCreateAppSettings().ReducedMotion;
    }

    /// <summary>
    /// Applies the reduced-motion preference live and persists it. When enabled, PbMotionFast
    /// resolves to TimeSpan.Zero so every consumer that binds to it (rather than checking a flag
    /// itself) is automatically instant - same "overwrite the resource" approach ApplyFontResource
    /// already uses.
    /// </summary>
    public void ApplyReducedMotion(bool enabled)
    {
        Application.Current!.Resources["PbMotionFast"] = enabled ? TimeSpan.Zero : DefaultMotionFast;
        Application.Current!.Resources["PbMotionSlow"] = enabled ? TimeSpan.Zero : DefaultMotionSlow;
        Application.Current!.Resources["PbMotionStandard"] = enabled ? TimeSpan.Zero : DefaultMotionStandard;
        Application.Current!.Resources["PbMotionLarge"] = enabled ? TimeSpan.Zero : DefaultMotionLarge;

        using var context = _contextFactory();
        var settings = context.GetOrCreateAppSettings();
        settings.ReducedMotion = enabled;
        context.SaveChanges();
    }

    private static readonly TimeSpan DefaultMotionFast = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan DefaultMotionSlow = TimeSpan.FromMilliseconds(700);

    /// <summary>docs/superpowers/specs/2026-09-04-navigation-transition-system-design.md - same
    /// zero-on-reduced-motion treatment as <see cref="DefaultMotionFast"/>/<see cref="DefaultMotionSlow"/> above.</summary>
    private static readonly TimeSpan DefaultMotionStandard = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan DefaultMotionLarge = TimeSpan.FromMilliseconds(320);

    /// <summary>Cross-platform replacement for GDI+'s InstalledFontCollection - prepends "System Default" (no override).</summary>
    public IReadOnlyList<string> GetInstalledFontFamilies()
    {
        var families = new List<string> { "System Default" };
        families.AddRange(SKFontManager.Default.FontFamilies.Distinct().OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
        return families;
    }

    /// <summary>
    /// Copies <paramref name="crpckPath"/> into <see cref="ThemePaths.InstalledDirectory"/>, extracts it,
    /// and validates its theme.json parses before accepting it. The key is the file's own base name.
    /// Method name deliberately unchanged by the theme-system rename (docs/superpowers/specs/
    /// 2026-09-16-theme-system-design.md) - the <c>.crpck</c> install/authoring mechanism itself is
    /// out of scope, deferred to a future "skins" initiative.
    /// </summary>
    public bool TryInstallSkin(string crpckPath, out string? error)
    {
        string key = Path.GetFileNameWithoutExtension(crpckPath);
        string destCrpck = Path.Combine(ThemePaths.InstalledDirectory, $"{key}.crpck");
        string extractDir = Path.Combine(ThemePaths.ExtractedDirectory, key);

        try
        {
            Directory.CreateDirectory(ThemePaths.InstalledDirectory);
            File.Copy(crpckPath, destCrpck, overwrite: true);

            if (Directory.Exists(extractDir))
            {
                Directory.Delete(extractDir, recursive: true);
            }

            ZipFile.ExtractToDirectory(destCrpck, extractDir);

            string themePath = Path.Combine(extractDir, "theme.json");
            if (!File.Exists(themePath) || JsonSerializer.Deserialize<ThemeDefinition>(File.ReadAllText(themePath), JsonOptions) is null)
            {
                error = "This file doesn't contain a valid theme.json.";
                TryDelete(destCrpck, extractDir);
                return false;
            }

            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            TryDelete(destCrpck, extractDir);
            return false;
        }
    }

    private static void TryDelete(string crpckPath, string extractDir)
    {
        try
        {
            if (File.Exists(crpckPath)) File.Delete(crpckPath);
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, recursive: true);
        }
        catch (IOException)
        {
        }
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

    /// <summary>
    /// Resolves + caches a theme's icon bitmap, null if undefined. Not consumed by any UI yet (§2 of
    /// the design spec) - the rail nav uses plain text labels. Misses are deliberately NOT cached,
    /// same self-healing rationale as <see cref="CoverImageCache"/>.
    /// </summary>
    public Bitmap? GetIcon(string themeKey, string iconKey)
    {
        string cacheKey = $"{themeKey}::{iconKey}";
        if (_iconCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var theme = TryLoadTheme(themeKey);
        if (theme is null || !theme.Icons.TryGetValue(iconKey, out string? relativePath))
        {
            return null;
        }

        try
        {
            Bitmap bitmap = BuiltInThemeKeys.Contains(themeKey, StringComparer.OrdinalIgnoreCase)
                ? new Bitmap(AssetLoader.Open(new Uri(BuiltInThemeAssetRoot(themeKey) + relativePath)))
                : new Bitmap(Path.Combine(ThemePaths.ExtractedDirectory, themeKey, relativePath));

            _iconCache[cacheKey] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
