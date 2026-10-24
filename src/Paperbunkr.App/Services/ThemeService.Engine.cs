using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Models;
using Paperbunkr.App.Theming;
using Paperbunkr.App.Theming.Runtime;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// The v2 theme engine behind <see cref="ThemeService"/>'s unchanged public surface (docs/superpowers/specs/2026-10-12-theme-system-v2-gap-analysis-and-plan.md).
/// A theme is loaded as a family (<see cref="ThemeCatalog"/>), resolved with the user's overrides (<see cref="ThemeResolver"/>), projected onto the
/// legacy <c>Pb*</c> resources and committed transactionally (<see cref="ThemeRuntimeApplier"/>). A legacy <c>theme.json</c> goes through the same
/// path as a single-variant family, which is what keeps every existing theme and installed <c>.crpck</c> working unchanged.
/// </summary>
public partial class ThemeService
{
    private readonly ThemeCatalog _catalog = new();
    private readonly IThemeOverrideStore _overrideStore;

    /// <summary>The theme currently live in the application resources (null before the first apply).</summary>
    public ResolvedTheme? Current { get; private set; }

    /// <summary>Raised after <see cref="Current"/> changes - for consumers that read resolved values (component skins, layouts, motion) rather than <c>Pb*</c> resources.</summary>
    public event Action<ResolvedTheme>? ResolvedThemeApplied;

    public ThemeCatalog Catalog => _catalog;

    public IThemeOverrideStore OverrideStore => _overrideStore;

    /// <summary>The findings from the most recent <see cref="TryInstallSkin"/> / <see cref="InstallThemePackage"/> call (empty before the first).</summary>
    public ThemeInstallResult? LastInstallResult { get; private set; }

    public ThemeService()
        : this(PaperbunkrDb.CreateContext, null, new FileThemeOverrideStore(FileThemeOverrideStore.DefaultPath()))
    {
        UseSplashStore(new Splash.SplashThemeStore(Splash.SplashThemeStore.DefaultPath()));
    }

    /// <summary>
    /// Test-only seam - production always uses the default ctor (the real per-user database and override file). Without an explicit store the
    /// overrides live in memory, so a test can never read or write the developer's real <c>theme-overrides.json</c>.
    /// </summary>
    internal ThemeService(Func<PaperbunkrDbContext> contextFactory, Func<DateTimeOffset>? now = null, IThemeOverrideStore? overrideStore = null)
    {
        _contextFactory = contextFactory;
        _now = now ?? (() => DateTimeOffset.Now);
        _overrideStore = overrideStore ?? new MemoryThemeOverrideStore();
    }

    // ===================== Catalog =====================

    /// <summary>Every selectable theme: one entry per variant of every family, built-ins first. A legacy theme is a family with one variant.</summary>
    public IReadOnlyList<ThemeSummary> GetAvailableThemes()
    {
        using var context = _contextFactory();
        string activeKey = context.GetOrCreateAppSettings().ActiveThemeKey;

        var summaries = new List<ThemeSummary>();
        foreach (var document in _catalog.LoadAll())
        {
            foreach (var variant in document.Variants)
            {
                var resolved = ThemeResolver.Resolve(document, variant.Id);
                summaries.Add(SummaryFrom(resolved, IsActiveKey(activeKey, document, variant)));
            }
        }

        return summaries;
    }

    /// <summary>A stored key names a variant either exactly ("paper:dark") or, for the family's first variant, by the bare family key.</summary>
    private static bool IsActiveKey(string activeKey, ThemeDocument document, ThemeVariantDefinition variant)
    {
        if (string.Equals(activeKey, document.ThemeKeyFor(variant), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var (familyKey, variantId) = ThemeDocument.SplitKey(activeKey);
        if (!string.Equals(familyKey, document.Key, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return variantId is null
            ? ReferenceEquals(variant, document.DefaultVariant)
            : string.Equals(variantId, variant.Id, StringComparison.OrdinalIgnoreCase)
                || (document.FindVariant(variantId) is null && ReferenceEquals(variant, document.DefaultVariant));
    }

    /// <summary>Preview-card and inspector data for one variant, read from its resolved tokens (no user overrides: the gallery shows the theme as its author made it).</summary>
    private static ThemeSummary SummaryFrom(ResolvedTheme theme, bool isActive)
    {
        IBrush Brush(string role) => new SolidColorBrush(theme.Color(role) ?? Colors.Transparent);
        Color bg = theme.Color("surface.canvas") ?? Colors.Black;
        var document = theme.Document;

        return new ThemeSummary
        {
            Key = theme.ThemeKey,
            Name = document.HasMultipleVariants ? $"{document.Name} · {theme.Variant.Name}" : document.Name,
            IsActive = isActive,
            Mode = theme.Mode,
            BackgroundBrush = Brush("surface.canvas"),
            ChromeBrush = Brush("surface.navigation"),
            AccentBrush = Brush("action.primary"),
            SurfaceBrush = Brush("surface.overlay"),
            TextMutedBrush = Brush("text.secondary"),
            Group = GroupOf(document, theme.Variant),
            ChromeSurfaceBrush = Brush("surface.navigation"),
            Surface1Brush = Brush("surface.default"),
            BorderBrush = Brush("border.default"),
            TextBrush = Brush("text.primary"),
            AccentTextBrush = Brush("text.accent"),
            BadgeBrush = Brush("badge.background"),
            SuccessBrush = Brush("status.success"),
            ChartBlueBrush = Brush("chart.series.1"),
            ChartVioletBrush = Brush("chart.series.2"),
            TextContrast = ThemeColorMath.ContrastRatio(theme.Color("text.primary") ?? Colors.White, bg),
            AccentContrast = ThemeColorMath.ContrastRatio(theme.Color("action.primary") ?? Colors.White, bg),
            FamilyKey = document.Key,
            FamilyId = document.Id,
            FamilyName = document.Name,
            VariantId = theme.VariantId,
            VariantName = theme.Variant.Name,
            VariantCount = document.Variants.Count,
            VariantKind = theme.Variant.Kind,
            IsLegacy = document.IsLegacy,
            IsBuiltIn = ThemeCatalog.IsBuiltIn(document.Key),
            Warnings = theme.Warnings.Select(w => w.Message).ToList(),
        };
    }

    /// <summary>
    /// The Appearance gallery group for a theme (docs/superpowers/specs/2026-10-12-connections-appearance-redesign-design.md): a theme whose
    /// <c>category</c> is "accessibility" goes under Accessibility whatever its mode, otherwise Light themes are Light and everything else Dark.
    /// </summary>
    internal static string GroupOf(ThemeDefinition theme)
    {
        if (string.Equals(theme.Category, "accessibility", StringComparison.OrdinalIgnoreCase))
        {
            return "Accessibility";
        }

        return string.Equals(theme.Mode, "Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
    }

    /// <summary>Same rule for a v2 family: the category, or a High Contrast variant, puts it under Accessibility.</summary>
    internal static string GroupOf(ThemeDocument document, ThemeVariantDefinition variant)
    {
        if (string.Equals(document.Category, "accessibility", StringComparison.OrdinalIgnoreCase) || variant.Kind == ThemeVariantKind.HighContrast)
        {
            return "Accessibility";
        }

        return variant.IsLight ? "Light" : "Dark";
    }

    /// <summary>
    /// The given theme in the original <see cref="ThemeDefinition"/> shape (as its author made it: no user overrides, no True Black). Throws if
    /// the theme doesn't exist or its <c>theme.json</c> cannot be used. Kept for callers that predate the v2 model.
    /// </summary>
    public ThemeDefinition LoadTheme(string key)
    {
        if (!_catalog.TryFind(key, out var document, out var variant))
        {
            throw new InvalidOperationException($"Theme '{key}' was not found or its theme.json is invalid.");
        }

        return LegacyThemeAdapter.FromResolved(ThemeResolver.Resolve(document, variant.Id));
    }

    // ===================== Resolve + apply =====================

    /// <summary>
    /// Resolves <paramref name="themeKey"/> the way it will be shown: with the user's overrides, the legacy accent override, and True Black
    /// unless <paramref name="suspendTrueBlack"/>. When True Black is on and the family ships a designed OLED variant, that variant is used
    /// instead of the black-everything transform (design doc decision D6) - the persisted key does not change.
    /// </summary>
    public ResolvedTheme ResolveTheme(string themeKey, AppSettings settings, bool suspendTrueBlack = false)
    {
        if (!_catalog.TryFind(themeKey, out var document, out var variant))
        {
            throw new InvalidOperationException($"Theme '{themeKey}' was not found or its theme.json is invalid.");
        }

        bool trueBlack = settings.TrueBlackDark && !suspendTrueBlack;
        if (trueBlack && !variant.IsLight && variant.Kind != ThemeVariantKind.Oled
            && document.Variants.FirstOrDefault(v => v.Kind == ThemeVariantKind.Oled) is { } oled)
        {
            variant = oled;
        }

        var environment = new ThemeEnvironment { TrueBlack = trueBlack, AccentOverrideHex = settings.AccentOverrideHex };
        return ThemeResolver.Resolve(document, variant.Id, _overrideStore.Load(), environment);
    }

    /// <summary>
    /// Commits a resolved theme to the live resources. Nothing is touched until the whole plan is built, and a failed commit restores what
    /// it overwrote (design doc section 10.1).
    /// </summary>
    private void ApplyResolved(ResolvedTheme theme)
    {
        var plan = ThemeRuntimeApplier.BuildPlan(theme, AddGlowToPlan);
        ContributeToPlan(theme, plan);
        ThemeRuntimeApplier.Commit(plan);

        _glowColor = plan.GlowColor;
        Current = theme;
        AfterCommit(theme);
        ResolvedThemeApplied?.Invoke(theme);
    }

    /// <summary>Extension point for the later layers (typography, component skins, materials) to add their resources to the same transaction.</summary>
    partial void ContributeToPlan(ResolvedTheme theme, ThemeApplyPlan plan);

    /// <summary>For state that is not a resource (the icon set, layouts, motion): published only once the commit succeeded.</summary>
    partial void AfterCommit(ResolvedTheme theme);

    private static bool AddGlowToPlan(Color glow, ThemeApplyPlan plan)
    {
        plan.Resources["PbGlowRing"] = BuildGlowRing(glow, _glowTier);

        // A fixed Normal-tier copy for consumers that are not a hover/focus affordance - the reader's bookmark-toggle pulse - so the
        // user's glow tier (even Off) never disables their feedback.
        plan.Resources["PbGlowPulseRing"] = BuildGlowRing(glow, GlowTierNormal);

        // Inside twin for controls clipped by their own container (a segmented control's pill), where an outside ring can't show.
        plan.Resources["PbGlowRingInset"] = BuildGlowRingInset(glow, _glowTier);
        return true;
    }

    /// <summary>
    /// Re-resolves and re-applies whatever is persisted as active, without changing any setting. Returns false (after applying the protected
    /// built-in default) when the active theme could not be resolved or applied.
    /// </summary>
    private bool Reapply(AppSettings settings, bool suspendTrueBlack = false)
    {
        try
        {
            ApplyResolved(ResolveTheme(settings.ActiveThemeKey, settings, suspendTrueBlack));
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ThemeApplyException)
        {
            ApplyProtectedFallback(settings, suspendTrueBlack);
            return false;
        }
    }

    /// <summary>The recovery path (design doc section 13.3): the built-in default, which is embedded in the app and cannot be uninstalled or broken by a package.</summary>
    private void ApplyProtectedFallback(AppSettings settings, bool suspendTrueBlack = false)
    {
        if (_catalog.Load(DefaultThemeKey) is not { } fallback)
        {
            return;
        }

        var environment = new ThemeEnvironment { TrueBlack = settings.TrueBlackDark && !suspendTrueBlack, AccentOverrideHex = settings.AccentOverrideHex };
        ApplyResolved(ThemeResolver.Resolve(fallback, null, UserThemeOverrides.Empty, environment));
    }

    /// <summary>Applies <paramref name="key"/> live to every screen and persists it as the active theme. The selection is saved only after the apply succeeded.</summary>
    public void ApplyTheme(string key)
    {
        using var context = _contextFactory();
        var settings = context.GetOrCreateAppSettings();

        var resolved = ResolveTheme(key, settings);
        ApplyResolved(resolved);

        settings.ActiveThemeKey = key;

        // Last-picked pair for Auto mode - updated on every manual apply, regardless of whether Auto mode is currently on, so it always
        // reflects the user's most recent real choice for that mode.
        if (resolved.IsLight)
        {
            settings.LastLightThemeKey = key;
        }
        else
        {
            settings.LastDarkThemeKey = key;
        }

        context.SaveChanges();

        ApplyFontResource(settings.SelectedFontFamily, resolved.Text("typography.body.family"));

        _iconCache.Clear();
        ThemeApplied?.Invoke();
        WindowBackdropRequested?.Invoke(resolved.Text("settings.windowBackdrop") ?? "None");
    }

    /// <summary>
    /// Re-applies whatever theme/font is already persisted in <see cref="AppSettings"/> - called once on startup. A persisted theme that is
    /// gone or broken falls back to the built-in default (and the default becomes the active theme), so a bad package can never stop the
    /// app from starting.
    /// </summary>
    public void ApplyPersistedSettings()
    {
        using var context = _contextFactory();
        var settings = context.GetOrCreateAppSettings();
        _glowTier = NormalizeGlowTier(settings.GlowTier);
        ApplyDensityResources(settings.DensityPreset);

        if (!Reapply(settings) && !string.Equals(settings.ActiveThemeKey, DefaultThemeKey, StringComparison.Ordinal))
        {
            settings.ActiveThemeKey = DefaultThemeKey;
            context.SaveChanges();
        }

        ApplyFontResource(settings.SelectedFontFamily, Current?.Text("typography.body.family"));
        Theming.Motion.MotionPolicy.RefreshSystemPreference();
        ApplyMotionResources(settings.MotionPreference);
        Theming.Motion.ThemeMotionRuntime.SetPreference(settings.MotionPreference);

        ThemeApplied?.Invoke();
    }

    /// <summary>
    /// Re-resolves the active theme with the current settings and overrides and applies it, without changing which theme is active. Called
    /// after a user override changes. Raises <see cref="ThemeApplied"/> so baked rasters (Home's cover wall, charts) follow.
    /// </summary>
    public void RefreshActiveTheme()
    {
        using var context = _contextFactory();
        var settings = context.GetOrCreateAppSettings();
        Reapply(settings, _trueBlackSuspended);
        ApplyFontResource(settings.SelectedFontFamily, Current?.Text("typography.body.family"));
        ThemeApplied?.Invoke();
    }

    private bool _trueBlackSuspended;

    /// <summary>Toggles true black live and persists it. Both directions re-resolve the theme, so "off" gets the theme's real values back with no cached state to keep in sync.</summary>
    public void SetTrueBlackDark(bool enabled)
    {
        using var context = _contextFactory();
        var settings = context.GetOrCreateAppSettings();
        settings.TrueBlackDark = enabled;
        context.SaveChanges();

        Reapply(settings, _trueBlackSuspended);
    }

    /// <summary>
    /// Live-only true-black suspend/resume for the Reader screens - never touches the persisted <see cref="AppSettings.TrueBlackDark"/> value,
    /// only the live resources, so the user's preference survives a reading session. <paramref name="suspend"/> true = the theme's real
    /// surfaces regardless of the setting; false = true black again if the setting is still on. Only the surfaces that actually differ are
    /// rewritten (the applier skips unchanged values), so entering a reader no longer restyles the whole window.
    /// </summary>
    public void SetTrueBlackReaderSuspend(bool suspend)
    {
        _trueBlackSuspended = suspend;
        using var context = _contextFactory();
        var settings = context.GetOrCreateAppSettings();
        Reapply(settings, suspend);
    }

    /// <summary>Persists and re-applies live immediately - null/whitespace clears the override, reverting to the active theme's own accent.</summary>
    public void SetAccentOverrideHex(string? hex)
    {
        string? normalized = string.IsNullOrWhiteSpace(hex) ? null : hex;

        using var context = _contextFactory();
        var settings = context.GetOrCreateAppSettings();
        settings.AccentOverrideHex = normalized;
        context.SaveChanges();

        Reapply(settings, _trueBlackSuspended);
    }

    // ===================== Auto switching (design doc decision D5) =====================

    /// <summary>
    /// The theme Auto mode should switch to: a variant of the active family with the wanted mode when the family has one, otherwise the last
    /// theme the user picked by hand for that mode. Null when there is nothing to switch to.
    /// </summary>
    private string? AutoSwitchTarget(AppSettings settings, bool wantLight)
    {
        if (_catalog.TryFind(settings.ActiveThemeKey, out var document, out _) && document.HasMultipleVariants)
        {
            var match = document.Variants.FirstOrDefault(v => v.IsLight == wantLight && v.Kind is ThemeVariantKind.Light or ThemeVariantKind.Dark)
                ?? document.Variants.FirstOrDefault(v => v.IsLight == wantLight);
            if (match is not null)
            {
                return document.ThemeKeyFor(match);
            }
        }

        return wantLight ? settings.LastLightThemeKey : settings.LastDarkThemeKey;
    }

    // ===================== Packages =====================

    /// <summary>
    /// Installs a <c>.crpck</c> through <see cref="ThemePackageInstaller"/>: staged, validated, then swapped in, so a package that fails leaves
    /// an already-installed theme of the same name untouched. The key is the file's own base name.
    /// </summary>
    public ThemeInstallResult InstallThemePackage(string crpckPath)
    {
        var result = ThemePackageInstaller.Install(crpckPath, ThemePaths.InstalledDirectory, ThemePaths.ExtractedDirectory);
        _catalog.Invalidate();
        LastInstallResult = result;
        return result;
    }

    /// <summary>The original entry point, kept for its callers; see <see cref="InstallThemePackage"/> for the full result.</summary>
    public bool TryInstallSkin(string crpckPath, out string? error)
    {
        var result = InstallThemePackage(crpckPath);
        error = result.Success ? null : result.Error ?? "This file doesn't contain a valid theme.json.";
        return result.Success;
    }

    /// <summary>
    /// Resolves + caches a theme's icon bitmap, null if undefined or unreadable. Misses are deliberately NOT cached, same self-healing
    /// rationale as <see cref="CoverImageCache"/>.
    /// </summary>
    public Bitmap? GetIcon(string themeKey, string iconKey)
    {
        string cacheKey = $"{themeKey}::{iconKey}";
        if (_iconCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        if (!_catalog.TryFind(themeKey, out var document, out var variant)
            || ThemeResolver.Resolve(document, variant.Id).Text("icons." + iconKey) is not { } relativePath)
        {
            return null;
        }

        try
        {
            using var stream = _catalog.OpenAsset(document.Key, relativePath);
            if (stream is null)
            {
                return null;
            }

            var bitmap = new Bitmap(stream);
            _iconCache[cacheKey] = bitmap;
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
