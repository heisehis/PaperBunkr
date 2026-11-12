using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Theming;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Appearance > Personalize (docs/superpowers/specs/2026-11-06-personalization-design.md): friendly front-ends over what already exists - the accent
/// override (one value shared with Customize's "All themes" accent, through the getter/setter the Preferences screen passes in) and the Global
/// <c>metrics.radius*</c> overrides - plus the five personalization settings, each applied live through <see cref="ThemeService.ApplyPersonalization"/>.
/// </summary>
public sealed partial class PersonalizeViewModel : ObservableObject
{
    public const string ThemeDefaultFont = "(Theme default)";

    private static readonly string[] RadiusPaths = { "metrics.radius", "metrics.radiusSm", "metrics.radiusLg" };

    private readonly ThemeService _themeService;
    private readonly Func<string?> _getAccent;
    private readonly Action<string?> _setAccent;
    private readonly Action<Action> _scheduleCorners;
    private readonly IReadOnlyList<string> _fontFamilies;
    private readonly PersonalizeLinks? _links;
    private bool _suppress;

    /// <param name="scheduleCorners">Debounces the Corners slider (three theme re-applies per value would stutter a drag); tests pass an immediate runner.</param>
    public PersonalizeViewModel(
        ThemeService themeService,
        Func<string?> getAccent,
        Action<string?> setAccent,
        IReadOnlyList<string> fontFamilies,
        Action<Action>? scheduleCorners = null,
        PersonalizeLinks? links = null)
    {
        _themeService = themeService;
        _getAccent = getAccent;
        _setAccent = setAccent;
        _scheduleCorners = scheduleCorners ?? Debounced(TimeSpan.FromMilliseconds(180));
        _fontFamilies = fontFamilies;
        _links = links;
        _themeService.OverridesChanged += RefreshCorners;
        Refresh();
    }

    /// <summary>The surface the accent is judged against for the contrast hint. A seam so tests need no Application.</summary>
    public Func<Color> SurfaceColorProvider { get; set; } = () =>
        Avalonia.Application.Current?.TryGetResource("PbSurface2Brush", null, out var r) == true && r is ISolidColorBrush b ? b.Color : Color.FromRgb(0x2A, 0x20, 0x30);

    // ===================== Accent =====================

    public IReadOnlyList<AccentSwatchItem> SwatchItems { get; } =
        CollectionPropertiesScreenViewModel.AccentSwatches.Select(hex => new AccentSwatchItem(hex)).ToList();

    /// <summary>The override as hex, or empty for the theme's own accent.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomAccent))]
    [NotifyPropertyChangedFor(nameof(HasAccentOverride))]
    [NotifyPropertyChangedFor(nameof(PickerColor))]
    private string _accentHex = string.Empty;

    public bool HasAccentOverride => !string.IsNullOrEmpty(AccentHex);

    public bool IsCustomAccent => HasAccentOverride && !SwatchItems.Any(s => string.Equals(s.Hex, AccentHex, StringComparison.OrdinalIgnoreCase));

    [ObservableProperty]
    private string _customAccentHex = string.Empty;

    [ObservableProperty]
    private string? _customAccentError;

    [ObservableProperty]
    private bool _isColorFlyoutOpen;

    [ObservableProperty]
    private bool _hasAccentContrastWarning;

    partial void OnAccentHexChanged(string value)
    {
        foreach (var swatch in SwatchItems)
        {
            swatch.IsSelected = string.Equals(swatch.Hex, value, StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrEmpty(value))
        {
            CustomAccentHex = value;
        }

        HasAccentContrastWarning = AccentColorInput.TryParseHex(value, out var accent) && AccentColorInput.IsLowContrast(accent, SurfaceColorProvider());

        if (!_suppress)
        {
            _setAccent(string.IsNullOrEmpty(value) ? null : value);
        }
    }

    /// <summary>Two-way colour for the flyout's picker; writing it sets the accent live.</summary>
    public Color PickerColor
    {
        get => AccentColorInput.TryParseHex(AccentHex, out var c) ? c : Color.FromRgb(0xC9, 0x80, 0x3F);
        set
        {
            string hex = AccentColorInput.ToHex(value);
            if (!string.Equals(hex, AccentHex, StringComparison.OrdinalIgnoreCase))
            {
                CustomAccentError = null;
                AccentHex = hex;
            }
        }
    }

    [RelayCommand]
    private void SetAccent(string? hex) => AccentHex = AccentColorInput.TryParseHex(hex, out var c) ? AccentColorInput.ToHex(c) : string.Empty;

    [RelayCommand]
    private void ResetAccent() => AccentHex = string.Empty;

    [RelayCommand]
    private void ToggleColorFlyout() => IsColorFlyoutOpen = !IsColorFlyoutOpen;

    [RelayCommand]
    private void ApplyCustomAccent()
    {
        if (AccentColorInput.TryParseHex(CustomAccentHex, out var color))
        {
            CustomAccentError = null;
            AccentHex = AccentColorInput.ToHex(color);
        }
        else
        {
            CustomAccentError = "Enter a hex colour like #C9803F";
        }
    }

    // ===================== Corners =====================

    /// <summary>0 square .. 50 the theme's own radii .. 100 twice them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CornersText))]
    private int _corners = PersonalizationMath.DefaultCorners;

    /// <summary>True while the three radius overrides are not on the slider's curve (edited in Customize).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CornersText))]
    private bool _isCustomCorners;

    public string CornersText => IsCustomCorners ? "Custom" : Corners switch
    {
        0 => "Square",
        PersonalizationMath.DefaultCorners => "Theme default",
        100 => "Round",
        _ => $"{Corners}%",
    };

    partial void OnCornersChanged(int value)
    {
        if (_suppress)
        {
            return;
        }

        IsCustomCorners = false;
        _scheduleCorners(() => CommitCorners(value));
        ManualLookChanged();
    }

    private void CommitCorners(int value)
    {
        if (value == PersonalizationMath.DefaultCorners)
        {
            ClearRadiusOverrides();
            return;
        }

        var (r, sm, lg) = PersonalizationMath.CornersToRadii(value);
        _themeService.SetOverride(ThemeOverrideScope.Global, RadiusPaths[0], Number(r));
        _themeService.SetOverride(ThemeOverrideScope.Global, RadiusPaths[1], Number(sm));
        _themeService.SetOverride(ThemeOverrideScope.Global, RadiusPaths[2], Number(lg));
    }

    private void ClearRadiusOverrides()
    {
        foreach (string path in RadiusPaths)
        {
            _themeService.SetOverride(ThemeOverrideScope.Global, path, null);
        }
    }

    [RelayCommand]
    private void ResetCorners()
    {
        ClearRadiusOverrides();
        RefreshCorners();
    }

    /// <summary>Reads the three Global radius overrides back into the slider (also after a Customize edit).</summary>
    private void RefreshCorners()
    {
        var values = RadiusPaths.Select(p => _themeService.GetOverride(ThemeOverrideScope.Global, p)).ToList();
        _suppress = true;
        try
        {
            if (values.All(v => v is null))
            {
                Corners = PersonalizationMath.DefaultCorners;
                IsCustomCorners = false;
                return;
            }

            double Parse(string? v, double fallback) =>
                double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : fallback;

            int? slider = PersonalizationMath.RadiiToCorners(
                Parse(values[0], PersonalizationMath.DefaultRadius),
                Parse(values[1], PersonalizationMath.DefaultRadiusSm),
                Parse(values[2], PersonalizationMath.DefaultRadiusLg));
            Corners = slider ?? PersonalizationMath.DefaultCorners;
            IsCustomCorners = slider is null;
        }
        finally
        {
            _suppress = false;
        }
    }

    // ===================== The five settings =====================

    public static IReadOnlyList<string> ElevationOptions { get; } = new[] { "Flat", "Soft", "Lifted" };

    public static IReadOnlyList<string> FontScaleOptions { get; } = new[] { "90%", "100%", "110%" };

    public static IReadOnlyList<string> SidebarStyleOptions { get; } = new[] { "Standard", "Pill", "Tint" };

    public static IReadOnlyList<string> FocusRingOptions { get; } = new[] { "Thin", "Normal", "Thick" };

    /// <summary>The installed fonts, read live (the Preferences screen fills the list after this view model may exist). "System Default" is not a heading font.</summary>
    public IReadOnlyList<string> HeadingFontOptions =>
        new[] { ThemeDefaultFont }.Concat(_fontFamilies.Where(f => f != SystemDefaultFont)).ToList();

    public const string SystemDefaultFont = "System Default";

    // ===================== Body font and ring strength (moved here from the Font group and Cover cosmetics) =====================

    /// <summary>The installed fonts for the body text, "System Default" first, read live.</summary>
    public IReadOnlyList<string> BodyFontOptions =>
        new[] { SystemDefaultFont }.Concat(_fontFamilies.Where(f => f != SystemDefaultFont)).ToList();

    /// <summary>Whether the screen passed the body-font and ring-strength links (always in the app; a bare test may not).</summary>
    public bool HasLinks => _links is not null;

    /// <summary>The body font, one value with the Preferences screen's font setting.</summary>
    [ObservableProperty]
    private string _bodyFontText = SystemDefaultFont;

    partial void OnBodyFontTextChanged(string value)
    {
        if (!_suppress && _links is not null && !string.IsNullOrWhiteSpace(value))
        {
            _links.SetBodyFont(value);
        }
    }

    /// <summary>The hover/focus ring strength (the old "Accent glow"), one value with the glow-tier setting.</summary>
    [ObservableProperty]
    private string _ringStrengthText = "Normal";

    partial void OnRingStrengthTextChanged(string value)
    {
        if (!_suppress && _links is not null && !string.IsNullOrWhiteSpace(value))
        {
            _links.SetRingStrength(value);
            ManualLookChanged();
        }
    }

    public IReadOnlyList<string> RingStrengthOptions => _links?.RingStrengthOptions ?? Array.Empty<string>();

    /// <summary>The four strength samples (Off .. Vivid) drawn with the live glow colour; owned and refreshed by the Preferences screen.</summary>
    public IEnumerable<GlowTierSample> RingSamples => _links?.RingSamples ?? Array.Empty<GlowTierSample>();

    [ObservableProperty]
    private string _elevationText = ElevationOptions[1];

    [ObservableProperty]
    private string _fontScaleText = FontScaleOptions[1];

    [ObservableProperty]
    private string _headingFontText = ThemeDefaultFont;

    [ObservableProperty]
    private string _sidebarStyleText = SidebarStyleOptions[0];

    [ObservableProperty]
    private string _focusRingText = FocusRingOptions[1];

    partial void OnElevationTextChanged(string value)
    {
        Persist(s => s.CardElevation = IndexOr(ElevationOptions, value, 1));
        ManualLookChanged();
    }

    partial void OnFontScaleTextChanged(string value) => Persist(s => s.FontScale = PersonalizationMath.FontScales[IndexOr(FontScaleOptions, value, 1)]);

    partial void OnHeadingFontTextChanged(string value) =>
        Persist(s => s.HeadingFontFamily = string.IsNullOrWhiteSpace(value) || value == ThemeDefaultFont ? null : value);

    partial void OnSidebarStyleTextChanged(string value)
    {
        Persist(s => s.SidebarStyle = (SidebarStyle)IndexOr(SidebarStyleOptions, value, 0));
        ManualLookChanged();
    }

    partial void OnFocusRingTextChanged(string value)
    {
        Persist(s => s.FocusRingThickness = IndexOr(FocusRingOptions, value, 1));
        ManualLookChanged();
    }

    private void Persist(Action<AppSettings> change)
    {
        if (!_suppress)
        {
            _themeService.ApplyPersonalization(change);
        }
    }

    private static int IndexOr(IReadOnlyList<string> options, string? value, int fallback)
    {
        int index = options.ToList().IndexOf(value ?? string.Empty);
        return index >= 0 ? index : fallback;
    }

    // ===================== Presets (docs/superpowers/specs/2026-11-06-looks-and-presets-design.md) =====================


    /// <summary>"Export my look…" / "Import a look…": the Preferences screen's own commands (one file with Customize).</summary>
    public System.Windows.Input.ICommand? ExportLookCommand => _links?.ExportLook;

    public System.Windows.Input.ICommand? ImportLookCommand => _links?.ImportLook;

    /// <summary>What the last export or import did, mirrored from the Preferences screen so it shows beside these buttons too.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLookFileMessage))]
    private string _lookFileMessage = string.Empty;

    public bool HasLookFileMessage => !string.IsNullOrEmpty(LookFileMessage);
    public IReadOnlyList<LookPresetCard> PresetCards { get; } = LookPresets.All.Select(p => new LookPresetCard(p)).ToList();

    /// <summary>"Quiet" / "Collector" / "Vivid" when the five look values are exactly that preset, else "Custom".</summary>
    [ObservableProperty]
    private string _lookLabel = "Custom";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoPresetCommand))]
    private bool _canUndoPreset;

    /// <summary>The look before the last preset, and the raw radius overrides then (so Undo also brings back corners edited in Customize).</summary>
    private (LookValues Look, Dictionary<string, string?> Radii)? _undo;

    private static readonly string[] LookRadiusPaths = { "metrics.radius", "metrics.radiusSm", "metrics.radiusLg" };

    [RelayCommand]
    private void ApplyPreset(string? name)
    {
        if (LookPresets.Find(name) is not { } preset)
        {
            return;
        }

        var radii = LookRadiusPaths.ToDictionary(p => p, p => _themeService.GetOverride(ThemeOverrideScope.Global, p));
        var previous = _themeService.ApplyLook(preset.Values);
        _links?.Reload?.Invoke();
        Refresh();
        _undo = (previous, radii);
        RefreshLook();
    }

    [RelayCommand(CanExecute = nameof(CanUndoPreset))]
    private void UndoPreset()
    {
        if (_undo is not { } undo)
        {
            return;
        }

        _undo = null;
        _themeService.SetGlobalOverridesWithoutRefresh(undo.Radii);
        _themeService.ApplyLook(undo.Look with { Corners = null }); // the radii were restored as they were, custom or not
        _links?.Reload?.Invoke();
        Refresh();
    }

    /// <summary>A change to one of the five look values by hand: the preset Undo no longer applies.</summary>
    private void ManualLookChanged()
    {
        if (_suppress)
        {
            return;
        }

        _undo = null;
        RefreshLook();
    }

    private LookValues CurrentLook() => new(
        IsCustomCorners ? null : Corners,
        IndexOr(ElevationOptions, ElevationText, 1),
        (SidebarStyle)IndexOr(SidebarStyleOptions, SidebarStyleText, 0),
        IndexOr(FocusRingOptions, FocusRingText, 1),
        _links is not null ? IndexOr(RingStrengthOptions, RingStrengthText, ThemeService.GlowTierNormal) : _themeService.GetGlowTier());

    private void RefreshLook()
    {
        var current = CurrentLook();
        foreach (var card in PresetCards)
        {
            card.IsSelected = card.Preset.Values == current;
        }

        LookLabel = LookPresets.Match(current)?.Name ?? "Custom";
        CanUndoPreset = _undo is not null;
    }

    // ===================== Load and reset =====================

    /// <summary>Reads everything back from the settings and the override store, without applying anything.</summary>
    public void Refresh()
    {
        // The font list may have been filled since the last read.
        OnPropertyChanged(nameof(HeadingFontOptions));
        OnPropertyChanged(nameof(BodyFontOptions));
        var state = _themeService.GetPersonalization();
        _suppress = true;
        try
        {
            AccentHex = _getAccent() is { } hex && AccentColorInput.TryParseHex(hex, out var c) ? AccentColorInput.ToHex(c) : string.Empty;
            ElevationText = ElevationOptions[state.CardElevation];
            FontScaleText = FontScaleOptions[Array.IndexOf(PersonalizationMath.FontScales, state.FontScale)];
            HeadingFontText = state.HeadingFont ?? ThemeDefaultFont;
            SidebarStyleText = SidebarStyleOptions[(int)state.SidebarStyle];
            FocusRingText = FocusRingOptions[state.FocusThickness];
            if (_links is not null)
            {
                BodyFontText = _links.GetBodyFont() is { Length: > 0 } body ? body : SystemDefaultFont;
                RingStrengthText = _links.GetRingStrength();
            }
        }
        finally
        {
            _suppress = false;
        }

        RefreshCorners();
        RefreshLook();
    }

    /// <summary>
    /// Back to the theme's own look for everything Personalize controls: the accent, the three corner radii, the five settings, the body font and the ring
    /// strength. Theme choice, density,
    /// glow, motion and any other Customize value are left alone.
    /// </summary>
    [RelayCommand]
    private void Reset()
    {
        AccentHex = string.Empty;
        ClearRadiusOverrides();
        _themeService.ApplyPersonalization(s =>
        {
            s.FontScale = 100;
            s.HeadingFontFamily = null;
            s.CardElevation = 1;
            s.SidebarStyle = SidebarStyle.Standard;
            s.FocusRingThickness = 1;
        });
        if (_links is not null)
        {
            _links.SetBodyFont(SystemDefaultFont);
            _links.SetRingStrength("Normal");
        }

        _undo = null;

        Refresh();
    }

    private static string Number(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static Action<Action> Debounced(TimeSpan delay)
    {
        Avalonia.Threading.DispatcherTimer? timer = null;
        Action? pending = null;
        return action =>
        {
            pending = action;
            if (timer is null)
            {
                timer = new Avalonia.Threading.DispatcherTimer { Interval = delay };
                timer.Tick += (_, _) =>
                {
                    timer!.Stop();
                    var run = pending;
                    pending = null;
                    run?.Invoke();
                };
            }

            timer.Stop();
            timer.Start();
        };
    }
}

/// <summary>
/// The two settings Personalize shows but the Preferences screen owns (the body font and the glow tier, moved here from the Font group and Cover cosmetics):
/// getters and setters go through the screen's own properties, so each stays one value with one apply path.
/// </summary>
public sealed record PersonalizeLinks(
    Func<string?> GetBodyFont,
    Action<string> SetBodyFont,
    Func<string> GetRingStrength,
    Action<string> SetRingStrength,
    IReadOnlyList<string> RingStrengthOptions,
    IEnumerable<GlowTierSample> RingSamples,
    Action? Reload = null,
    System.Windows.Input.ICommand? ExportLook = null,
    System.Windows.Input.ICommand? ImportLook = null);
