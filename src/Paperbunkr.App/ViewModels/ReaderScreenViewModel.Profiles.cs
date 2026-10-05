using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Reader;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>One profile in the reader drawer's profile list ("Standard" is the row with a null id).</summary>
public sealed partial class ReaderProfileRow : ObservableObject
{
    private readonly Action<ReaderProfileRow>? _select;

    public ReaderProfileRow(int id, string name, bool isBuiltIn, Action<ReaderProfileRow>? select = null)
    {
        Id = id;
        Name = name;
        IsBuiltIn = isBuiltIn;
        _select = select;
    }

    /// <summary>The <see cref="Workspace"/> id, or <see cref="ReaderProfileSelector.StandardSessionId"/> for the Standard row.</summary>
    public int Id { get; }

    public string Name { get; }

    public bool IsBuiltIn { get; }

    [ObservableProperty]
    private bool _isActive;

    /// <summary>Applies this profile for the current reading visit.</summary>
    [RelayCommand]
    private void Select() => _select?.Invoke(this);
}

/// <summary>
/// Reader profiles (docs/superpowers/specs/2026-09-25-comic-reader-profiles-design.md): the session switch (hotkey, drawer, palette), capture from the live reader, and the
/// "use for this series / as my default" pointers. Kept in its own file so the already-large reader view model does not grow further.
/// </summary>
public partial class ReaderScreenViewModel
{
    private readonly WorkspaceService _profileService = new();

    /// <summary>Raised for a short, low-stakes message ("Profile: Manga night"); <see cref="MainViewModel"/> wires it to the shared toast host.</summary>
    public event Action<ToastRequest>? ToastRequested;

    /// <summary>Raised after this view model saved a new profile, so the Preferences list reloads.</summary>
    public event Action? ProfilesChanged;

    /// <summary>The shared naming overlay (<c>MainViewModel.PromptWorkspaceName</c>); a no-op until wired.</summary>
    public Action<string?, Action<string>> PromptForName { get; set; } = (_, _) => { };

    /// <summary>The Standard row plus every reader profile, in the order the hotkey cycles them.</summary>
    public ObservableCollection<ReaderProfileRow> Profiles { get; } = new();

    /// <summary>The profile in effect, or "Standard" when there is none.</summary>
    public string ActiveProfileName => _profile?.Name ?? "Standard";

    /// <summary>True when the active profile is one the user made (a built-in cannot be updated).</summary>
    public bool CanUpdateActiveProfile => _profile?.Row is { IsBuiltIn: false };

    /// <summary>True when the loaded issue's series points at a profile.</summary>
    public bool HasSeriesProfile => _seriesProfileId is not null;

    private int? _seriesProfileId;

    /// <summary>Rebuilds <see cref="Profiles"/> from the database and marks the active one. Called when the drawer's profile section needs fresh data.</summary>
    public void RefreshProfiles()
    {
        int activeId = _profile?.Row?.Id ?? ReaderProfileSelector.StandardSessionId;
        Profiles.Clear();
        Profiles.Add(new ReaderProfileRow(ReaderProfileSelector.StandardSessionId, "Standard", isBuiltIn: true, SelectProfile));
        foreach (var row in _profileService.List(WorkspaceScreen.Reader))
        {
            Profiles.Add(new ReaderProfileRow(row.Id, row.Name, row.IsBuiltIn, SelectProfile));
        }

        foreach (var row in Profiles)
        {
            row.IsActive = row.Id == activeId;
        }
    }

    /// <summary>Called whenever the profile in effect may have changed.</summary>
    private void NotifyProfileChanged()
    {
        OnPropertyChanged(nameof(ActiveProfileName));
        OnPropertyChanged(nameof(CanUpdateActiveProfile));
        int activeId = _profile?.Row?.Id ?? ReaderProfileSelector.StandardSessionId;
        foreach (var row in Profiles)
        {
            row.IsActive = row.Id == activeId;
        }
    }

    /// <summary>Applies <paramref name="row"/> for this reading visit (the drawer list, the palette and the hotkey all end here).</summary>
    private void SelectProfile(ReaderProfileRow row) => ApplySessionProfile(row.Id, row.Name);

    /// <summary>
    /// Choosing a profile in the reader is a session-only, live change: it wins over series and issue overrides for the fields the profile sets and writes no <c>Issue</c> or <c>Series</c>
    /// row (the display values are set directly, never through the persisting fit/adjust commands). It survives loading another issue and is cleared when the reader is left.
    /// </summary>
    public void ApplySessionProfile(int profileId, string name)
    {
        var before = (PagedTapZoneLayout, PagedTapZoneInvert, ContinuousTapZoneLayout, ContinuousTapZoneInvert);
        _sessionProfileId = profileId;
        ReapplyProfile();
        ToastRequested?.Invoke(new ToastRequest($"Profile: {(profileId == ReaderProfileSelector.StandardSessionId ? "Standard" : name)}"));

        if (before != (PagedTapZoneLayout, PagedTapZoneInvert, ContinuousTapZoneLayout, ContinuousTapZoneInvert))
        {
            ShowTapZoneFlash();
        }
    }

    /// <summary>Re-resolves the profile for the loaded issue and pushes every value it can change into the open reader, without persisting anything.</summary>
    private void ReapplyProfile()
    {
        if (_loadedIssueId is not int issueId || _loadedSeriesId is not int seriesId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext(includeRemote: true);
        var issue = context.Issues.Find(issueId);
        var series = context.Series.Find(seriesId);
        if (issue is null || series is null)
        {
            return;
        }

        var settings = context.GetOrCreateAppSettings();
        _profile = ReaderProfileSelector.Resolve(context, series, settings, _sessionProfileId);
        var effective = _profile.Effective;

        _autoHideChromeEnabled = effective.ReaderAutoHideChrome;
        ChromeHoverMode = effective.ReaderChromeHoverMode;
        FitMode = ReaderProfileResolution.FitMode(_profile.SessionState, issue, series, effective);
        AutoRotate = ReaderProfileResolution.AutoRotate(_profile.SessionState, issue, series, effective);
        ApplyAdjustmentDefaults(issue, effective);
        ApplyImageQualityDefaults(issue, effective, pushIfChanged: true);
        RefreshDisplaySettings();
    }

    /// <summary>Sets the four adjustment values and their "global default" baselines from the (profile-overlaid) settings, the issue's stored deltas and a session profile's absolute values, without writing any override.</summary>
    private void ApplyAdjustmentDefaults(Issue issue, AppSettings effective)
    {
        var session = _profile?.SessionState;
        var brightness = ReaderProfileResolution.Adjust(session?.Brightness, issue.BrightnessOverride, effective.DefaultBrightness);
        var contrast = ReaderProfileResolution.Adjust(session?.Contrast, issue.ContrastOverride, effective.DefaultContrast);
        var saturation = ReaderProfileResolution.Adjust(session?.Saturation, issue.SaturationOverride, effective.DefaultSaturation);
        var gamma = ReaderProfileResolution.Adjust(session?.Gamma, issue.GammaOverride, effective.DefaultGamma);

        _brightnessGlobalDefault = brightness.GlobalDefault;
        _contrastGlobalDefault = contrast.GlobalDefault;
        _saturationGlobalDefault = saturation.GlobalDefault;
        _gammaGlobalDefault = gamma.GlobalDefault;
        _suppressAdjustmentPersist = true;
        Brightness = brightness.Value;
        Contrast = contrast.Value;
        Saturation = saturation.Value;
        Gamma = gamma.Value;
        _suppressAdjustmentPersist = false;
    }

    /// <summary>Leaving the reader ends the visit's profile choice (called from <c>GoBack</c>).</summary>
    private void ClearSessionProfile()
    {
        _sessionProfileId = null;
        ClearImageQualitySession();
    }

    /// <summary>The Standard-first list the hotkey cycles through.</summary>
    [RelayCommand]
    private void NextProfile()
    {
        var rows = new List<(int Id, string Name)> { (ReaderProfileSelector.StandardSessionId, "Standard") };
        rows.AddRange(_profileService.List(WorkspaceScreen.Reader).Select(r => (r.Id, r.Name)));
        int currentId = _sessionProfileId ?? _profile?.Row?.Id ?? ReaderProfileSelector.StandardSessionId;
        int index = rows.FindIndex(r => r.Id == currentId);
        var next = rows[(index + 1) % rows.Count];
        ApplySessionProfile(next.Id, next.Name);
    }

    // ===================== Capture and persist =====================

    /// <summary>A snapshot of what is on screen now: the live fit, rotate, layout, transition and adjustments plus the resolved background, margin, chrome and tap zone settings. Null before an issue is loaded.</summary>
    internal ReaderProfileState? CaptureProfileState()
    {
        if (_profile is not { } profile)
        {
            return null;
        }

        var e = profile.Effective;
        return new ReaderProfileState(
            FitMode: FitMode,
            AutoRotate: AutoRotate,
            PageLayoutMode: EffectivePageLayoutMode,
            PageTransitionStyle: PageTransitionStyle,
            PageTransitionDurationMs: PageTransitionDurationMs,
            Brightness: Brightness,
            Contrast: Contrast,
            Saturation: Saturation,
            Gamma: Gamma,
            ImageBackgroundMode: e.ImageBackgroundMode,
            BackgroundColor: e.BackgroundColor,
            BackgroundTexture: e.BackgroundTexture,
            PageMarginEnabled: e.PageMarginEnabled,
            PageMarginPercentWidth: e.PageMarginPercentWidth,
            ReaderAutoHideChrome: e.ReaderAutoHideChrome,
            ReaderChromeHoverMode: e.ReaderChromeHoverMode,
            PagedTapZoneLayout: PagedTapZoneLayout,
            PagedTapZoneInvert: PagedTapZoneInvert,
            ContinuousTapZoneLayout: ContinuousTapZoneLayout,
            ContinuousTapZoneInvert: ContinuousTapZoneInvert,
            AutoLevels: AutoLevels,
            Sharpen: Sharpen,
            AutoCrop: AutoCrop);
    }

    /// <summary>Asks for a name, then saves the current look as a new reader profile (an existing user profile of that name is overwritten) and switches to it.</summary>
    [RelayCommand]
    private void SaveCurrentAsProfile() => PromptForName(null, SaveCurrentAsProfileNamed);

    /// <summary>The naming prompt's callback; also the seam tests call.</summary>
    internal void SaveCurrentAsProfileNamed(string name)
    {
        if (CaptureProfileState() is not { } state || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        string json = ReaderProfileStateJson.Serialize(state);
        var existing = _profileService.List(WorkspaceScreen.Reader)
            .FirstOrDefault(w => !w.IsBuiltIn && string.Equals(w.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        int id;
        if (existing is not null)
        {
            _profileService.UpdateState(existing.Id, json);
            id = existing.Id;
        }
        else
        {
            id = _profileService.Create(WorkspaceScreen.Reader, name.Trim(), json).Id;
        }

        RefreshProfiles();
        ProfilesChanged?.Invoke();
        ApplySessionProfile(id, name.Trim());
    }

    /// <summary>Re-snapshots the active profile with what is on screen (user profiles only).</summary>
    [RelayCommand]
    private void UpdateActiveProfile()
    {
        if (_profile?.Row is not { IsBuiltIn: false } row || CaptureProfileState() is not { } state)
        {
            return;
        }

        _profileService.UpdateState(row.Id, ReaderProfileStateJson.Serialize(state));
        ToastRequested?.Invoke(new ToastRequest($"Updated profile “{row.Name}”"));
    }

    /// <summary>The loaded issue's series opens with the active profile from now on ("Standard" clears it).</summary>
    [RelayCommand]
    private void UseProfileForSeries()
    {
        if (_loadedSeriesId is not int seriesId)
        {
            return;
        }

        int? profileId = _profile?.Row?.Id;
        using var context = PaperbunkrDb.CreateContext(includeRemote: true);
        if (context.Series.Find(seriesId) is not { } series)
        {
            return;
        }

        series.ReaderProfileId = profileId;
        context.SaveChanges();
        _seriesProfileId = profileId;
        OnPropertyChanged(nameof(HasSeriesProfile));
        ToastRequested?.Invoke(new ToastRequest(profileId is null ? $"“{series.Name}” uses your standard settings" : $"“{series.Name}” opens with {ActiveProfileName}"));
    }

    /// <summary>Removes the loaded issue's series pointer.</summary>
    [RelayCommand]
    private void ClearSeriesProfile()
    {
        if (_loadedSeriesId is not int seriesId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext(includeRemote: true);
        if (context.Series.Find(seriesId) is { } series)
        {
            series.ReaderProfileId = null;
            context.SaveChanges();
        }

        _seriesProfileId = null;
        OnPropertyChanged(nameof(HasSeriesProfile));
    }

    /// <summary>The active profile becomes every series' default (Standard clears it).</summary>
    [RelayCommand]
    private void UseProfileAsDefault()
    {
        int? profileId = _profile?.Row?.Id;
        using var context = PaperbunkrDb.CreateContext(includeRemote: true);
        context.GetOrCreateAppSettings().DefaultReaderProfileId = profileId;
        context.SaveChanges();
        ToastRequested?.Invoke(new ToastRequest(profileId is null ? "Your standard settings are the default" : $"{ActiveProfileName} is now your default profile"));
    }
}
