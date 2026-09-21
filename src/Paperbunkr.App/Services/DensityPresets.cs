using Avalonia;

namespace Paperbunkr.App.Services;

/// <summary>
/// Compact / Comfortable / Spacious (docs/superpowers/specs/2026-09-21-cosmetics-pitch-2-design.md #17). A preset scales a small, named set
/// of spacing tokens together - the Library list and details row padding and the sidebar item padding - through resources
/// (<c>PbListRowPadding</c>, <c>PbDetailsRowPadding</c>, <c>PbSidebarItemPadding</c>), so nothing hardcodes the sizes. <see cref="Comfortable"/>
/// reproduces exactly what the app looked like before presets existed. Deliberately NOT a global type scale (Avalonia has no cheap global font
/// scale) and NOT the poster-grid density, which keeps its own slider.
/// </summary>
public static class DensityPresets
{
    public const int Compact = 0;
    public const int Comfortable = 1;
    public const int Spacious = 2;

    public static readonly string[] Names = { "Compact", "Comfortable", "Spacious" };

    public sealed record Values(Thickness ListRowPadding, Thickness DetailsRowPadding, Thickness SidebarItemPadding);

    public static int Normalize(int preset) => preset is >= Compact and <= Spacious ? preset : Comfortable;

    public static Values For(int preset) => Normalize(preset) switch
    {
        Compact => new Values(new Thickness(6), new Thickness(0, 1), new Thickness(8, 4)),
        Spacious => new Values(new Thickness(14), new Thickness(0, 6), new Thickness(10, 10)),
        _ => new Values(new Thickness(10), new Thickness(0, 3), new Thickness(8, 7)),
    };

    public static string NameOf(int preset) => Names[Normalize(preset)];

    public static int FromName(string? name)
    {
        int index = System.Array.IndexOf(Names, name);
        return index >= 0 ? index : Comfortable;
    }
}
