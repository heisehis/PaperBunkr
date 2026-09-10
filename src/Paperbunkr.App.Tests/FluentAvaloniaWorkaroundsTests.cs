using System;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Platform;
using FluentAvalonia.Styling;
using Paperbunkr.App.Services;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="FluentAvaloniaWorkarounds.SuppressColorValuesChangedHandler"/> is the fix for the
/// 2026-09-10 dropdown-open freeze (see that method's doc comment for the captured stack). These
/// assert it actually detaches FluentAvalonia's <c>ColorValuesChanged</c> subscription and is
/// safe to call in every state.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class FluentAvaloniaWorkaroundsTests
{
    /// <summary>Reads the multicast delegate behind <c>IPlatformSettings.ColorValuesChanged</c>
    /// by locating the backing field on the concrete platform-settings instance.</summary>
    private static Delegate? GetColorValuesChangedField(IPlatformSettings settings)
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
                return (Delegate?)field.GetValue(settings);
            }
        }

        return null;
    }

    private static int HandlerCount(IPlatformSettings settings) =>
        GetColorValuesChangedField(settings)?.GetInvocationList().Length ?? 0;

    private static bool AnyFluentAvaloniaHandler(IPlatformSettings settings) =>
        GetColorValuesChangedField(settings)?.GetInvocationList()
            .Any(d => d.Target is FluentAvaloniaTheme) ?? false;

    [Fact]
    public void DetachesFluentAvaloniaSubscription_AndIsIdempotent()
    {
        var app = Application.Current!;
        var settings = app.PlatformSettings!;

        var theme = new FluentAvaloniaTheme(); // ctor subscribes to settings.ColorValuesChanged
        app.Styles.Add(theme);
        try
        {
            Assert.True(AnyFluentAvaloniaHandler(settings), "precondition: FA subscribed on construction");
            int before = HandlerCount(settings);

            FluentAvaloniaWorkarounds.SuppressColorValuesChangedHandler();

            Assert.False(AnyFluentAvaloniaHandler(settings), "FA handler still attached after suppression");
            Assert.Equal(before - 1, HandlerCount(settings));

            // Second call must be a harmless no-op, not throw or over-remove.
            FluentAvaloniaWorkarounds.SuppressColorValuesChangedHandler();
            Assert.Equal(before - 1, HandlerCount(settings));
        }
        finally
        {
            app.Styles.Remove(theme);
        }
    }

    [Fact]
    public void NoFluentAvaloniaTheme_DoesNotThrow()
    {
        Assert.False(Application.Current!.Styles.OfType<FluentAvaloniaTheme>().Any());
        FluentAvaloniaWorkarounds.SuppressColorValuesChangedHandler(); // must not throw
    }
}
