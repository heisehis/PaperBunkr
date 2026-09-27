using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Paperbunkr.App.Models;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services.Reader;

/// <summary>
/// Lays a reader profile over the global settings (docs/superpowers/specs/2026-09-25-comic-reader-profiles-design.md section 1). The result is a <b>detached copy</b>: the tracked
/// <see cref="AppSettings"/> entity is never touched, so nothing from a profile can be written back by a later <c>SaveChanges</c>.
/// </summary>
public static class ReaderProfileOverlay
{
    private static readonly PropertyInfo[] CopyableProperties = typeof(AppSettings)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p is { CanRead: true, CanWrite: true } && p.GetIndexParameters().Length == 0)
        .ToArray();

    /// <summary>Returns <paramref name="settings"/> itself when there is no profile (callers only read it), otherwise a copy with the profile's non-null fields applied.</summary>
    public static AppSettings Apply(AppSettings settings, ReaderProfileState? profile)
    {
        if (profile is null)
        {
            return settings;
        }

        var copy = Clone(settings);
        if (profile.FitMode is { } fit) copy.DefaultPageFitMode = fit;
        if (profile.AutoRotate is { } rotate) copy.DefaultAutoRotate = rotate;
        if (profile.PageLayoutMode is { } layout) copy.DefaultPageLayoutMode = layout;
        if (profile.PageTransitionStyle is { } style) copy.PageTransitionStyle = style;
        if (profile.PageTransitionDurationMs is { } duration) copy.PageTransitionDurationMs = duration;
        if (profile.Brightness is { } brightness) copy.DefaultBrightness = brightness;
        if (profile.Contrast is { } contrast) copy.DefaultContrast = contrast;
        if (profile.Saturation is { } saturation) copy.DefaultSaturation = saturation;
        if (profile.Gamma is { } gamma) copy.DefaultGamma = gamma;
        if (profile.ImageBackgroundMode is { } mode) copy.ImageBackgroundMode = mode;
        if (profile.BackgroundColor is { } color) copy.BackgroundColor = color;
        if (profile.BackgroundTexture is { } texture) copy.BackgroundTexture = texture;
        if (profile.PageMarginEnabled is { } margin) copy.PageMarginEnabled = margin;
        if (profile.PageMarginPercentWidth is { } marginWidth) copy.PageMarginPercentWidth = marginWidth;
        if (profile.ReaderAutoHideChrome is { } autoHide) copy.ReaderAutoHideChrome = autoHide;
        if (profile.ReaderChromeHoverMode is { } hover) copy.ReaderChromeHoverMode = hover;
        if (profile.PagedTapZoneLayout is { } pagedLayout) copy.PagedTapZoneLayout = pagedLayout;
        if (profile.PagedTapZoneInvert is { } pagedInvert) copy.PagedTapZoneInvert = pagedInvert;
        if (profile.ContinuousTapZoneLayout is { } continuousLayout) copy.ContinuousTapZoneLayout = continuousLayout;
        if (profile.ContinuousTapZoneInvert is { } continuousInvert) copy.ContinuousTapZoneInvert = continuousInvert;
        if (profile.ShowSessionHud is { } hud) copy.ShowSessionHud = hud;
        if (profile.WarmShiftEnabled is { } warm) copy.WarmShiftEnabled = warm;
        if (profile.WarmShiftStrength is { } warmStrength) copy.WarmShiftStrength = warmStrength;
        if (profile.GuidedView is { } guided) copy.GuidedViewOnOpen = guided;
        if (profile.AutoLevels is { } autoLevels) copy.DefaultAutoLevels = autoLevels;
        if (profile.Sharpen is { } sharpen) copy.DefaultSharpen = sharpen;
        if (profile.AutoCrop is { } autoCrop) copy.AutoCropMargins = autoCrop;
        return copy;
    }

    /// <summary>A detached shallow copy of every settable column.</summary>
    internal static AppSettings Clone(AppSettings source)
    {
        var copy = new AppSettings();
        foreach (var property in CopyableProperties)
        {
            property.SetValue(copy, property.GetValue(source));
        }

        return copy;
    }
}

/// <summary>Which profile a reader visit uses, and the per-field precedence between a session profile, the issue and series overrides, and the settings a pointer profile has already been laid over.</summary>
public static class ReaderProfileSelector
{
    /// <summary>The session choice "Standard": explicitly no profile this visit, ignoring the series pointer and the default.</summary>
    public const int StandardSessionId = 0;

    /// <summary>The outcome of picking a profile.</summary>
    /// <param name="Row">The profile's row, or null for plain settings.</param>
    /// <param name="IsSession">True when it was chosen in this reader visit (it then wins over series and issue overrides for the fields it sets).</param>
    public readonly record struct Selection(Workspace? Row, bool IsSession);

    /// <summary>
    /// A session choice wins (Standard = none); otherwise the series' pointer, then the default. An id with no matching row is skipped, falling to the next candidate. Only rows of
    /// <see cref="WorkspaceScreen.Reader"/> count.
    /// </summary>
    public static Selection Select(IReadOnlyList<Workspace> readerRows, int? sessionId, int? seriesProfileId, int? defaultProfileId)
    {
        Workspace? Find(int? id) => id is { } value ? readerRows.FirstOrDefault(w => w.Id == value && w.Screen == WorkspaceScreen.Reader) : null;

        if (sessionId == StandardSessionId)
        {
            return new Selection(null, false);
        }

        if (Find(sessionId) is { } session)
        {
            return new Selection(session, true);
        }

        return new Selection(Find(seriesProfileId) ?? Find(defaultProfileId), false);
    }

    /// <summary>What the reader works from for one visit: the selection, its parsed state and the settings with the profile laid over.</summary>
    public sealed record Context(Workspace? Row, ReaderProfileState? State, bool IsSession, AppSettings Effective)
    {
        /// <summary>The profile chosen this visit (which wins over overrides), or null.</summary>
        public ReaderProfileState? SessionState => IsSession ? State : null;

        public string? Name => Row?.Name;
    }

    /// <summary>Loads the candidate rows from the database and resolves the profile for <paramref name="series"/>.</summary>
    public static Context Resolve(PaperbunkrDbContext context, Series? series, AppSettings settings, int? sessionId)
    {
        var ids = new[] { sessionId, series?.ReaderProfileId, settings.DefaultReaderProfileId }
            .Where(id => id is > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var rows = ids.Count == 0
            ? new List<Workspace>()
            : context.Workspaces.Where(w => w.Screen == WorkspaceScreen.Reader && ids.Contains(w.Id)).ToList();

        var selection = Select(rows, sessionId, series?.ReaderProfileId, settings.DefaultReaderProfileId);
        var state = selection.Row is null ? null : ReaderProfileStateJson.Deserialize(selection.Row.StateJson);
        return new Context(selection.Row, state, selection.IsSession, ReaderProfileOverlay.Apply(settings, state));
    }
}

/// <summary>
/// Per-field precedence for the reader's display values (docs/superpowers/specs/2026-09-25-comic-reader-profiles-design.md section 1): a <b>session</b> profile's fields, then the issue and
/// series overrides, then the settings with a pointer profile already laid over them. Fit mode and auto-rotate reuse <see cref="ReaderDefaultsResolver"/>.
/// </summary>
public static class ReaderProfileResolution
{
    public static ImageFitMode FitMode(ReaderProfileState? session, Issue? issue, Series? series, AppSettings effective) =>
        session?.FitMode ?? ReaderDefaultsResolver.EffectiveFitMode(issue, series, effective);

    public static bool AutoRotate(ReaderProfileState? session, Issue? issue, Series? series, AppSettings effective) =>
        session?.AutoRotate ?? ReaderDefaultsResolver.EffectiveAutoRotate(issue, series, effective);

    public static PageLayoutMode LayoutMode(ReaderProfileState? session, Issue? issue, Series? series, AppSettings effective) =>
        session?.PageLayoutMode ?? issue?.PageLayoutModeOverride ?? series?.PageLayoutMode ?? effective.DefaultPageLayoutMode;

    /// <summary>The "global default" the issue's delta is measured from, and the resulting value.</summary>
    public readonly record struct Adjustment(double GlobalDefault, double Value);

    /// <summary>A session profile's absolute value replaces both the default and the issue's delta; otherwise the (possibly pointer-profile) default plus the issue's delta.</summary>
    public static Adjustment Adjust(double? sessionValue, float? issueDelta, double effectiveDefault) =>
        sessionValue is { } absolute
            ? new Adjustment(absolute, absolute)
            : new Adjustment(effectiveDefault, effectiveDefault + (issueDelta ?? 0));
}
