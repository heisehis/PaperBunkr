using System.Linq;
using Paperbunkr.App.Services;
using Paperbunkr.Plugins.Theme;

namespace Paperbunkr.App.Plugins;

/// <summary>Real adapter for <see cref="IThemePlugin"/> (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §4) - just the active theme key. Renamed from <c>CurrentSkinKey</c> by docs/superpowers/specs/2026-09-16-theme-system-design.md.</summary>
public sealed class PaperbunkrThemePlugin : IThemePlugin
{
    public string CurrentThemeKey
    {
        get
        {
            using var context = PaperbunkrDb.CreateContext();
            return context.AppSettings.FirstOrDefault()?.ActiveThemeKey ?? ThemeService.DefaultThemeKey;
        }
    }
}
