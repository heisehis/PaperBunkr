using System;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Platform;
using FluentAvalonia.Styling;

namespace Paperbunkr.App.Services;

/// <summary>
/// Targeted workarounds for framework-level defects that only bite this project's runtime
/// environment. Kept in one place, each with the evidence that motivated it, so they can be
/// removed wholesale once the upstream fix lands.
/// </summary>
internal static class FluentAvaloniaWorkarounds
{
    /// <summary>
    /// Detaches <see cref="FluentAvaloniaTheme"/> from
    /// <see cref="IPlatformSettings.ColorValuesChanged"/>.
    ///
    /// <para><b>Why (freeze stack captured 2026-09-10, <c>0.3.0-beta+19c3dd0</c>):</b> opening any
    /// <c>ComboBox</c> / <c>AutoCompleteBox</c> dropdown permanently froze the UI thread. Chain:</para>
    /// <list type="number">
    ///   <item><c>Popup.Open()</c> builds an <c>OverlayPopupHost</c>, which is a <c>TopLevel</c>;
    ///     its constructor synchronously re-raises <c>ColorValuesChanged</c>.</item>
    ///   <item><see cref="FluentAvaloniaTheme"/>'s handler responds - unconditionally, on every
    ///     Windows firing, whether or not High Contrast is active (verified against FA
    ///     <c>master</c>) - by rewriting 8 <c>SystemColor*</c> entries in its HighContrast
    ///     <c>ResourceDictionary</c>. The dictionary indexer raises <c>ResourcesChanged</c>
    ///     whether or not the value actually changed.</item>
    ///   <item>That tree-wide <c>ResourcesChanged</c> fires <em>while the popup is still inside
    ///     <c>Popup.Open()</c></em> and re-evaluates the popup's TwoWay <c>bool</c>
    ///     <c>IsOpen</c> ⇄ <c>IsDropDownOpen</c> <c>TemplateBinding</c>, which then oscillates
    ///     forever. UI thread pegged, never recovers; <c>FreezeWatchdog</c> re-fires every 10s.</item>
    /// </list>
    ///
    /// <para><b>Why removing the handler is safe here:</b> Paperbunkr commits to a single fixed
    /// visual identity - <c>RequestedThemeVariant="Dark"</c>, <c>PreferSystemTheme="False"</c>,
    /// <c>PreferUserAccentColor="False"</c>, explicit <c>CustomAccentColor</c> (App.axaml). The
    /// handler's only observable effect for this app is the harmful HighContrast rewrite.</para>
    ///
    /// <para>Reflection because FA exposes no opt-out. Idempotent, best-effort, and it now also
    /// nulls the whole <c>ColorValuesChanged</c> backing delegate as a fallback if the precise
    /// <c>-=</c> did not land (e.g. FA re-subscribed, or wrapped its handler). Logs what it did to
    /// <c>startup.log</c> so a persisting freeze can be told apart from "the workaround never ran".</para>
    /// </summary>
    public static void SuppressColorValuesChangedHandler()
    {
        try
        {
            var theme = Application.Current?.Styles.OfType<FluentAvaloniaTheme>().FirstOrDefault();
            var settings = Application.Current?.PlatformSettings;
            if (settings is null)
            {
                DiagnosticsService.LogMilestone("FA color-handler suppression: PlatformSettings unavailable - skipped");
                return;
            }

            var field = FindColorValuesChangedField(settings);
            var before = (field?.GetValue(settings) as Delegate)?.GetInvocationList().Length ?? -1;

            bool removedPrecise = false;
            if (theme is not null)
            {
                var method = typeof(FluentAvaloniaTheme).GetMethod(
                    "OnPlatformColorValuesChanged",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (method is not null)
                {
                    var handler = (EventHandler<PlatformColorValues>)Delegate.CreateDelegate(
                        typeof(EventHandler<PlatformColorValues>), theme, method);
                    settings.ColorValuesChanged -= handler;
                    removedPrecise = true;
                }
            }

            var afterPrecise = (field?.GetValue(settings) as Delegate)?.GetInvocationList().Length ?? -1;

            // Fallback: if the precise -= didn't reduce the count (FA changed, re-subscribed, or a
            // wrapper), and every remaining subscriber is FA's own, wipe the backing delegate. The
            // app never needs ColorValuesChanged (fixed theme/accent), so this is safe app-wide.
            bool wiped = false;
            if (field is not null && afterPrecise > 0)
            {
                var current = field.GetValue(settings) as Delegate;
                var list = current?.GetInvocationList() ?? Array.Empty<Delegate>();
                if (list.Length > 0 && list.All(d =>
                        d.Target is FluentAvaloniaTheme ||
                        (d.Target?.GetType().Namespace?.StartsWith("FluentAvalonia", StringComparison.Ordinal) ?? false)))
                {
                    field.SetValue(settings, null);
                    wiped = true;
                }
            }

            var afterFinal = (field?.GetValue(settings) as Delegate)?.GetInvocationList().Length ?? -1;
            DiagnosticsService.LogMilestone(
                $"FA color-handler suppression: theme={(theme is null ? "missing" : "found")} " +
                $"field={(field is null ? "not-found" : field.Name)} count {before}->{afterPrecise}->{afterFinal} " +
                $"precise={removedPrecise} wiped={wiped}");
        }
        catch (Exception ex)
        {
            DiagnosticsService.LogMilestone($"FA color-handler suppression failed: {ex.GetType().Name} {ex.Message}");
        }
    }

    private static FieldInfo? FindColorValuesChangedField(IPlatformSettings settings)
    {
        for (var type = settings.GetType(); type is not null; type = type.BaseType)
        {
            var field = type
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(f =>
                    typeof(Delegate).IsAssignableFrom(f.FieldType) &&
                    f.Name.Contains("ColorValuesChanged", StringComparison.Ordinal));
            if (field is not null)
            {
                return field;
            }
        }

        return null;
    }
}
