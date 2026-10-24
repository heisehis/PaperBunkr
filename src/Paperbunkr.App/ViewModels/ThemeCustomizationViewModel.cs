using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.App.Theming;

namespace Paperbunkr.App.ViewModels;

/// <summary>One editable value in Appearance > Theme > Customize: a token, the user's own value for it at the chosen scope, and where the live value comes from.</summary>
public sealed partial class ThemeOverrideRowViewModel : ObservableObject
{
    private readonly Action<ThemeOverrideRowViewModel> _apply;
    private bool _loading;

    public ThemeOverrideRowViewModel(string label, string path, ThemeValueKind kind, string description, Action<ThemeOverrideRowViewModel> apply)
    {
        Label = label;
        Path = path;
        Kind = kind;
        Description = description;
        _apply = apply;
    }

    public string Label { get; }

    /// <summary>The token path ("action.primary") or component path this row edits.</summary>
    public string Path { get; }

    public ThemeValueKind Kind { get; }

    public string Description { get; }

    public bool IsColor => Kind == ThemeValueKind.Color;

    /// <summary>What the user typed for this scope. Empty = no override here (the box then shows the live value as its watermark).</summary>
    [ObservableProperty]
    private string _valueText = string.Empty;

    /// <summary>The live resolved value, shown as the watermark so an empty box still says what is in effect.</summary>
    [ObservableProperty]
    private string _resolvedText = string.Empty;

    [ObservableProperty]
    private IBrush? _swatch;

    /// <summary>"From the theme", "Your setting for all themes", ...</summary>
    [ObservableProperty]
    private string _sourceText = string.Empty;

    [ObservableProperty]
    private bool _isOverridden;

    /// <summary>True while the box holds something that is not a valid value for this token; nothing is applied until it is.</summary>
    [ObservableProperty]
    private bool _isInvalid;

    public string AutomationName => $"{Label} override";

    partial void OnValueTextChanged(string value)
    {
        if (!_loading)
        {
            _apply(this);
        }
    }

    [RelayCommand]
    private void Reset() => ValueText = string.Empty;

    /// <summary>Loads state from the service without triggering an apply.</summary>
    internal void Load(string? overrideValue, ResolvedToken? resolved)
    {
        _loading = true;
        try
        {
            ValueText = overrideValue ?? string.Empty;
            IsOverridden = !string.IsNullOrEmpty(overrideValue);
            IsInvalid = false;
            ResolvedText = resolved is null ? string.Empty : Format(resolved.Value);
            Swatch = resolved is { Value.Kind: ThemeValueKind.Color } ? new SolidColorBrush(resolved.Value.Color) : null;
            SourceText = ThemeCustomizationViewModel.DescribeSource(resolved?.Source);
        }
        finally
        {
            _loading = false;
        }
    }

    private static string Format(ThemeValue value) => value.Kind == ThemeValueKind.Number
        ? value.Number.ToString("0.##", CultureInfo.InvariantCulture)
        : value.ToString();
}

/// <summary>
/// Appearance > Theme > Customize (docs/superpowers/specs/2026-10-12-theme-system-v2-gap-analysis-and-plan.md, phase 2; design doc section 12.1):
/// a small, fixed set of useful values the user can change for every theme, for the active theme family, or for the active variant only, each
/// with its own reset. Not a theme editor - it writes user overrides, never the installed theme.
/// </summary>
public sealed partial class ThemeCustomizationViewModel : ObservableObject
{
    public const string ScopeAllThemes = "All themes";
    public const string ScopeThisTheme = "This theme";
    public const string ScopeThisVariant = "This variant";

    private readonly ThemeService _service;
    private readonly Func<string?> _getGlobalAccent;
    private readonly Action<string?> _setGlobalAccent;
    private bool _refreshing;

    public ThemeCustomizationViewModel(ThemeService service, Func<string?> getGlobalAccent, Action<string?> setGlobalAccent)
    {
        _service = service;
        _getGlobalAccent = getGlobalAccent;
        _setGlobalAccent = setGlobalAccent;

        Rows = new ObservableCollection<ThemeOverrideRowViewModel>
        {
            Row("Accent", "action.primary", ThemeValueKind.Color, "Primary buttons, selection and links."),
            Row("Background", "surface.canvas", ThemeValueKind.Color, "The app background."),
            Row("Cards", "surface.raised", ThemeValueKind.Color, "Cards and tiles on a panel."),
            Row("Text", "text.primary", ThemeValueKind.Color, "Body text."),
            Row("Secondary text", "text.secondary", ThemeValueKind.Color, "Descriptions and labels."),
            Row("Borders", "border.default", ThemeValueKind.Color, "Hairlines and control outlines."),
            Row("Success", "status.success", ThemeValueKind.Color, "Connected, done, passing."),
            Row("Danger", "status.danger", ThemeValueKind.Color, "Errors and destructive actions."),
            Row("Corner radius", "metrics.radius", ThemeValueKind.Number, "How round cards and buttons are, in pixels."),
            Row("Primary button", ThemeTokenPaths.Component("button.primary", "normal", "background"), ThemeValueKind.Color, "The fill of the main action button. Its hover and pressed colours follow."),
            Row("Chip corners", ThemeTokenPaths.Component("chip", "normal", "radius"), ThemeValueKind.Number, "How round chips and tags are, in pixels."),
        };

        _service.OverridesChanged += Refresh;
        Refresh();
    }

    public IReadOnlyList<string> ScopeOptions { get; } = new[] { ScopeAllThemes, ScopeThisTheme, ScopeThisVariant };

    /// <summary>Which of the user's layers the rows below read and write.</summary>
    [ObservableProperty]
    private string _scopeText = ScopeAllThemes;

    public ObservableCollection<ThemeOverrideRowViewModel> Rows { get; }

    /// <summary>Contrast notes and fallbacks for the theme as currently shown. Report-only.</summary>
    public ObservableCollection<string> Notes { get; } = new();

    [ObservableProperty]
    private bool _hasNotes;

    [ObservableProperty]
    private bool _boostContrast;

    [ObservableProperty]
    private bool _hasOverridesInScope;

    public IReadOnlyList<string> EffectQualityOptions { get; } = new[] { nameof(ThemeEffectQuality.High), nameof(ThemeEffectQuality.Balanced), nameof(ThemeEffectQuality.Performance) };

    /// <summary>How much of a theme's materials and motion is drawn: High = everything, Balanced = no continuous motion, Performance = solid fills and no theme motion.</summary>
    [ObservableProperty]
    private string _effectQualityText = nameof(ThemeEffectQuality.High);

    /// <summary>Whether the active theme may rearrange screens. Off by default (decision D11).</summary>
    [ObservableProperty]
    private bool _allowThemeLayouts;

    /// <summary>"This theme rearranges Home." / "This theme does not rearrange any screen."</summary>
    [ObservableProperty]
    private string _layoutSummary = string.Empty;

    [ObservableProperty]
    private bool _hasLayoutAdjustments;

    [ObservableProperty]
    private string _scopeSummary = string.Empty;

    public ThemeOverrideScope Scope => ScopeText switch
    {
        ScopeThisTheme => ThemeOverrideScope.Family,
        ScopeThisVariant => ThemeOverrideScope.Variant,
        _ => ThemeOverrideScope.Global,
    };

    partial void OnScopeTextChanged(string value) => Refresh();

    partial void OnBoostContrastChanged(bool value)
    {
        if (!_refreshing)
        {
            _service.SetBoostContrast(value);
        }
    }

    partial void OnEffectQualityTextChanged(string value)
    {
        if (!_refreshing && Enum.TryParse<ThemeEffectQuality>(value, ignoreCase: true, out var quality))
        {
            _service.SetEffectQuality(quality);
        }
    }

    partial void OnAllowThemeLayoutsChanged(bool value)
    {
        if (!_refreshing)
        {
            _service.SetAllowThemeLayouts(value);
        }
    }

    /// <summary>Removes the user's own adjustments to the active theme's layouts.</summary>
    [RelayCommand]
    private void ResetLayout()
    {
        _service.ResetLayoutPatches();
        Refresh();
    }

    private ThemeOverrideRowViewModel Row(string label, string path, ThemeValueKind kind, string description) => new(label, path, kind, description, Apply);

    /// <summary>The accent for "All themes" is the long-standing accent setting, so there is one global accent, not two.</summary>
    private bool IsGlobalAccent(ThemeOverrideRowViewModel row) => Scope == ThemeOverrideScope.Global && row.Path == "action.primary";

    private void Apply(ThemeOverrideRowViewModel row)
    {
        string text = row.ValueText.Trim();

        // A hex colour is only a value once it is complete (#RRGGBB or #AARRGGBB); "#336" parses as shorthand but is somebody mid-typing.
        bool incompleteHex = row.IsColor && text.StartsWith('#') && text.Length is not (7 or 9);
        if (text.Length > 0 && (incompleteHex || !ThemeService.IsValidOverride(row.Path, text)))
        {
            row.IsInvalid = true;   // keep what was typed; apply nothing until it is a real value
            return;
        }

        row.IsInvalid = false;
        if (IsGlobalAccent(row))
        {
            _setGlobalAccent(text.Length == 0 ? null : text);
            Refresh();
            return;
        }

        _service.SetOverride(Scope, row.Path, text.Length == 0 ? null : text);
    }

    [RelayCommand]
    private void ResetScope()
    {
        if (Scope == ThemeOverrideScope.Global && !string.IsNullOrEmpty(_getGlobalAccent()))
        {
            _setGlobalAccent(null);
        }

        _service.ResetOverrides(Scope);
        Refresh();
    }

    /// <summary>Re-reads every row from the service. Called when the scope, the active theme or any override changes.</summary>
    public void Refresh()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            var current = _service.Current;
            int count = 0;
            foreach (var row in Rows)
            {
                string? value = IsGlobalAccent(row) ? _getGlobalAccent() : _service.GetOverride(Scope, row.Path);
                if (!string.IsNullOrEmpty(value))
                {
                    count++;
                }

                // A row holding something invalid keeps it (the user is mid-edit); every other row follows the service.
                if (!row.IsInvalid)
                {
                    row.Load(value, _service.ExplainToken(row.Path));
                }
            }

            HasOverridesInScope = count > 0;
            ScopeSummary = count switch
            {
                0 => Scope == ThemeOverrideScope.Global ? "Nothing changed for all themes." : "Nothing changed here.",
                1 => "1 value changed.",
                _ => $"{count} values changed.",
            };

            BoostContrast = _service.GetBoostContrast();
            EffectQualityText = _service.GetEffectQuality().ToString();
            AllowThemeLayouts = _service.GetAllowThemeLayouts();
            HasLayoutAdjustments = _service.CountLayoutPatches() > 0;

            var screens = _service.ActiveThemeLayoutScreens;
            LayoutSummary = screens.Count == 0
                ? "This theme does not rearrange any screen."
                : (AllowThemeLayouts ? "This theme rearranges " : "This theme can rearrange ") + string.Join(", ", screens.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)) + ".";

            Notes.Clear();
            foreach (var note in _service.GetActiveDiagnostics().Where(d => d.Severity != ThemeDiagnosticSeverity.Information).Select(d => d.Message).Distinct())
            {
                Notes.Add(note);
            }

            HasNotes = Notes.Count > 0;
        }
        finally
        {
            _refreshing = false;
        }
    }

    public static string DescribeSource(ThemeValueSource? source) => source switch
    {
        ThemeValueSource.FamilyAuthored or ThemeValueSource.VariantAuthored => "From the theme",
        ThemeValueSource.Derived => "Worked out from the theme",
        ThemeValueSource.UserGlobal => "Your setting for all themes",
        ThemeValueSource.UserFamily => "Your setting for this theme",
        ThemeValueSource.UserVariant => "Your setting for this variant",
        ThemeValueSource.UserComponent => "Your setting for this control",
        ThemeValueSource.Accessibility => "Raised by Boost contrast",
        ThemeValueSource.TrueBlack => "True black",
        ThemeValueSource.BuiltInFallback => "Built-in default",
        _ => string.Empty,
    };
}
