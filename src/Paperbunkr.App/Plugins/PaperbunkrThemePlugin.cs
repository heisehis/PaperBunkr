using System.Linq;
using Paperbunkr.App.Services;
using Paperbunkr.Plugins.Theme;

namespace Paperbunkr.App.Plugins;

/// <summary>Real adapter for <see cref="IThemePlugin"/> (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §4) - just the active skin key, no dark-mode flag (Paperbunkr's skin system doesn't track one).</summary>
public sealed class PaperbunkrThemePlugin : IThemePlugin
{
    public string CurrentSkinKey
    {
        get
        {
            using var context = PaperbunkrDb.CreateContext();
            return context.AppSettings.FirstOrDefault()?.ActiveSkinKey ?? SkinService.DefaultSkinKey;
        }
    }
}
