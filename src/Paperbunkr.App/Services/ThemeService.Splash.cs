using System;
using Avalonia;
using Avalonia.Threading;
using Paperbunkr.App.Services.Splash;
using Paperbunkr.App.Theming;
using Paperbunkr.App.Theming.Runtime;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// Keeps <c>splash-theme.json</c> current (docs/superpowers/specs/2026-10-22-splash-theme-redesign-design.md): the startup splash shows
/// before the database is ready, so it reads this snapshot of the theme instead of the theme settings.
/// </summary>
public partial class ThemeService
{
    private SplashThemeStore? _splashStore;
    private bool _splashSavePending;

    /// <summary>Starts writing the snapshot to <paramref name="store"/> after every theme apply. The production constructor uses the real file;
    /// tests opt in with an in-memory store, so they never touch the developer's file.</summary>
    internal void UseSplashStore(SplashThemeStore store)
    {
        if (_splashStore is null)
        {
            ResolvedThemeApplied += _ => ScheduleSplashSnapshot();
        }

        _splashStore = store;
    }

    /// <summary>Deferred one tick: <see cref="ApplyTheme"/> saves the new key and the light/dark pair after the apply event, and a burst of
    /// applies (startup, an Auto switch) writes once.</summary>
    private void ScheduleSplashSnapshot()
    {
        if (_splashSavePending)
        {
            return;
        }

        if (Application.Current is null)
        {
            SaveSplashSnapshot();
            return;
        }

        _splashSavePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _splashSavePending = false;
            SaveSplashSnapshot();
        });
    }

    /// <summary>Writes the current theme plus Auto mode's light and dark targets. Best-effort: a failure is logged and never reaches the caller.</summary>
    public void SaveSplashSnapshot()
    {
        if (_splashStore is null)
        {
            return;
        }

        try
        {
            using var context = _contextFactory();
            var settings = context.GetOrCreateAppSettings();

            var current = PaletteFor(Current) ?? PaletteForKey(settings.ActiveThemeKey, settings) ?? SplashPalette.Default;
            var light = PaletteForKey(AutoSwitchTarget(settings, wantLight: true), settings);
            var dark = PaletteForKey(AutoSwitchTarget(settings, wantLight: false), settings);

            _splashStore.Save(new SplashThemeSnapshot(
                settings.ThemeAutoMode, settings.ThemeScheduledDarkHour, settings.ThemeScheduledLightHour, current, light, dark));
        }
        catch (Exception ex)
        {
            DiagnosticsService.LogMilestone($"Splash theme snapshot failed ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    private SplashPalette? PaletteForKey(string? key, AppSettings settings)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        try
        {
            return PaletteFor(ResolveTheme(key, settings));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ThemeApplyException)
        {
            return null;
        }
    }

    private static SplashPalette? PaletteFor(ResolvedTheme? theme) =>
        theme is null ? null : SplashPalette.FromTheme(LegacyThemeAdapter.FromResolved(theme));
}
