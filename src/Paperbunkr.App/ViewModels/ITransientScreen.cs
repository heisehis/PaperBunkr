namespace Paperbunkr.App.ViewModels;

/// <summary>
/// A screen whose view is built fresh on every visit instead of being kept by <see cref="Controls.RouteHost"/>
/// (docs/superpowers/specs/2026-10-20-motion-system-v2-navigation-design.md Q14): the readers, which own decode pipelines and a WebView.
/// </summary>
public interface ITransientScreen
{
}
