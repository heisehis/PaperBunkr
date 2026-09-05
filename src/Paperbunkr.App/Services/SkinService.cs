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
using FluentAvalonia.Styling;
using Paperbunkr.App.Models;
using Paperbunkr.Data;
using SkiaSharp;

namespace Paperbunkr.App.Services;

/// <summary>
/// Skin/theme install-apply-persist mechanism (docs/superpowers/specs/2026-08-07-preferences-skin-system-design.md
/// §3). The built-in "default" skin is an embedded <c>avares://</c> resource following the exact
/// same <c>theme.json</c> schema as an installed <c>.crpck</c> - switching skins and the app's own
/// default look go through identical code, no special-cased path.
/// </summary>
public class SkinService
{
    public const string DefaultSkinKey = "default";

    private const string DefaultSkinAssetRoot = "avares://Paperbunkr.App/Assets/Skins/default/";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly Dictionary<string, Bitmap> _iconCache = new();
    private readonly Func<PaperbunkrDbContext> _contextFactory;

    public SkinService()
        : this(PaperbunkrDb.CreateContext)
    {
    }

    /// <summary>Test-only seam - production always uses the default ctor (the real per-user database).</summary>
    internal SkinService(Func<PaperbunkrDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public IReadOnlyList<SkinSummary> GetAvailableSkins()
    {
        using var context = _contextFactory();
        string activeKey = context.GetOrCreateAppSettings().ActiveSkinKey;

        var summaries = new List<SkinSummary>();

        var defaultTheme = TryLoadSkin(DefaultSkinKey);
        summaries.Add(new SkinSummary
        {
            Key = DefaultSkinKey,
            Name = defaultTheme?.Name ?? "Default",
            IsActive = activeKey == DefaultSkinKey,
        });

        if (Directory.Exists(SkinPaths.ExtractedDirectory))
        {
            foreach (string dir in Directory.GetDirectories(SkinPaths.ExtractedDirectory).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string key = Path.GetFileName(dir);
                var theme = TryLoadSkin(key);
                if (theme is not null)
                {
                    summaries.Add(new SkinSummary { Key = key, Name = theme.Name, IsActive = activeKey == key });
                }
            }
        }

        return summaries;
    }

    /// <summary>Parses the given skin's <c>theme.json</c>. Throws if the skin doesn't exist or is invalid.</summary>
    public SkinTheme LoadSkin(string key)
    {
        return TryLoadSkin(key) ?? throw new InvalidOperationException($"Skin '{key}' was not found or its theme.json is invalid.");
    }

    private static SkinTheme? TryLoadSkin(string key)
    {
        try
        {
            string json = key == DefaultSkinKey
                ? ReadEmbeddedText(DefaultSkinAssetRoot + "theme.json")
                : File.ReadAllText(Path.Combine(SkinPaths.ExtractedDirectory, key, "theme.json"));

            return JsonSerializer.Deserialize<SkinTheme>(json, JsonOptions);
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

    /// <summary>Applies <paramref name="key"/>'s colors/spacing/radius live to every screen and persists it as the active skin.</summary>
    public void ApplySkin(string key)
    {
        ApplySkinResources(LoadSkin(key));

        using var context = _contextFactory();
        var settings = context.GetOrCreateAppSettings();
        settings.ActiveSkinKey = key;
        context.SaveChanges();

        _iconCache.Clear();
    }

    /// <summary>Re-applies whatever skin/font is already persisted in <see cref="Data.Entities.AppSettings"/> - called once on startup.</summary>
    public void ApplyPersistedSettings()
    {
        using var context = _contextFactory();
        var settings = context.GetOrCreateAppSettings();
        ApplySkinResources(LoadSkin(settings.ActiveSkinKey));
        ApplyFontResource(settings.SelectedFontFamily);
        Application.Current!.Resources["PbMotionFast"] = settings.ReducedMotion ? TimeSpan.Zero : DefaultMotionFast;
        Application.Current!.Resources["PbMotionSlow"] = settings.ReducedMotion ? TimeSpan.Zero : DefaultMotionSlow;
        Application.Current!.Resources["PbMotionStandard"] = settings.ReducedMotion ? TimeSpan.Zero : DefaultMotionStandard;
        Application.Current!.Resources["PbMotionLarge"] = settings.ReducedMotion ? TimeSpan.Zero : DefaultMotionLarge;
    }

    private static void ApplySkinResources(SkinTheme theme)
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
        SetColorAndBrush(resources, "PbSurface0", theme.Colors.Surface0);
        SetColorAndBrush(resources, "PbSurface1", theme.Colors.Surface1);
        SetColorAndBrush(resources, "PbSurface2", theme.Colors.Surface2);
        SetColorAndBrush(resources, "PbSurface3", theme.Colors.Surface3);
        SetColorAndBrush(resources, "PbGlow", theme.Colors.Glow);

        resources["PbHeroGradientStartColor"] = Color.Parse(theme.Colors.HeroGradientStart);
        resources["PbHeroGradientEndColor"] = Color.Parse(theme.Colors.HeroGradientEnd);

        ApplyAccentColor(theme.Colors.Accent);

        resources["PbSpacingUnit"] = theme.SpacingUnit;

        // CornerRadius, not double - see the matching comment on PbRadius in App.axaml for why.
        resources["PbRadius"] = new CornerRadius(theme.Radius);
        resources["PbRadiusSm"] = new CornerRadius(theme.RadiusSm);
        resources["PbRadiusLg"] = new CornerRadius(theme.RadiusLg);
    }

    /// <summary>
    /// Pushes the skin's accent into FluentAvalonia's stock-control accent system
    /// (<see cref="FluentAvaloniaTheme.CustomAccentColor"/>), which seeds SystemAccentColor and its
    /// 6 tonal variants. This is the FluentAvalonia equivalent of the old
    /// <c>FluentTheme.Palettes</c> <c>Accent="{DynamicResource PbAccentColor}"</c> binding - it
    /// keeps a live skin switch recoloring stock controls (TextBox focus, ToggleSwitch, ScrollBar,
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

    /// <summary>Returns the currently persisted font family override, or null (app default).</summary>
    public string? GetSelectedFontFamily()
    {
        using var context = _contextFactory();
        return context.GetOrCreateAppSettings().SelectedFontFamily;
    }

    /// <summary>Applies a font override live (null/"System Default" clears it) and persists the choice.</summary>
    public void ApplyFont(string? fontFamilyName)
    {
        string? normalized = fontFamilyName is null or "System Default" ? null : fontFamilyName;
        ApplyFontResource(normalized);

        using var context = _contextFactory();
        var settings = context.GetOrCreateAppSettings();
        settings.SelectedFontFamily = normalized;
        context.SaveChanges();
    }

    /// <summary>
    /// The new bundled-default body font (docs/superpowers/specs/2026-08-24-design-language-
    /// foundation-design.md Typography section) - what "no override" resolves to. Deliberately a
    /// live default value, not an absent key, unlike the pre-this-phase behavior: ApplyFontResource
    /// used to Remove("PbFontFamily") when there was no override, relying on Avalonia's graceful
    /// fallback to the FluentTheme/OS default for an unresolved DynamicResource. Bebas Neue
    /// (PbDisplayFontFamily, App.axaml) is a separate, non-overridable resource for hero/heading
    /// text only - it never goes through this override mechanism.
    /// </summary>
    private static readonly FontFamily DefaultFontFamily =
        new("avares://Paperbunkr.App/Assets/Fonts/#Source Serif 4, Georgia, serif");

    private static void ApplyFontResource(string? fontFamilyName)
    {
        var resources = Application.Current!.Resources;
        resources["PbFontFamily"] = fontFamilyName is null ? DefaultFontFamily : new FontFamily(fontFamilyName);
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
    /// Copies <paramref name="crpckPath"/> into <see cref="SkinPaths.InstalledDirectory"/>, extracts it,
    /// and validates its theme.json parses before accepting it. The key is the file's own base name.
    /// </summary>
    public bool TryInstallSkin(string crpckPath, out string? error)
    {
        string key = Path.GetFileNameWithoutExtension(crpckPath);
        string destCrpck = Path.Combine(SkinPaths.InstalledDirectory, $"{key}.crpck");
        string extractDir = Path.Combine(SkinPaths.ExtractedDirectory, key);

        try
        {
            Directory.CreateDirectory(SkinPaths.InstalledDirectory);
            File.Copy(crpckPath, destCrpck, overwrite: true);

            if (Directory.Exists(extractDir))
            {
                Directory.Delete(extractDir, recursive: true);
            }

            ZipFile.ExtractToDirectory(destCrpck, extractDir);

            string themePath = Path.Combine(extractDir, "theme.json");
            if (!File.Exists(themePath) || JsonSerializer.Deserialize<SkinTheme>(File.ReadAllText(themePath), JsonOptions) is null)
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
        Directory.CreateDirectory(SkinPaths.InstalledDirectory);
        try
        {
            Process.Start(new ProcessStartInfo { FileName = SkinPaths.InstalledDirectory, UseShellExecute = true });
        }
        catch
        {
            // No shell/file-manager available (e.g. some CI/headless environments) - nothing more we can do.
        }
    }

    /// <summary>
    /// Resolves + caches a skin's icon bitmap, null if undefined. Not consumed by any UI yet (§2 of
    /// the design spec) - the rail nav uses plain text labels. Misses are deliberately NOT cached,
    /// same self-healing rationale as <see cref="CoverImageCache"/>.
    /// </summary>
    public Bitmap? GetIcon(string skinKey, string iconKey)
    {
        string cacheKey = $"{skinKey}::{iconKey}";
        if (_iconCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var theme = TryLoadSkin(skinKey);
        if (theme is null || !theme.Icons.TryGetValue(iconKey, out string? relativePath))
        {
            return null;
        }

        try
        {
            Bitmap bitmap = skinKey == DefaultSkinKey
                ? new Bitmap(AssetLoader.Open(new Uri(DefaultSkinAssetRoot + relativePath)))
                : new Bitmap(Path.Combine(SkinPaths.ExtractedDirectory, skinKey, relativePath));

            _iconCache[cacheKey] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
