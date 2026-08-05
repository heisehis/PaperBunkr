using Avalonia;
using Avalonia.Media;

namespace Paperbunkr.App.Models;

/// <summary>
/// Sample data for a Library grid card. Mirrors the "covers" sample array from the
/// "Paperbunkr App" Claude Design wireframe (project 43c40b25) - placeholder data until
/// real Series/Issue records exist (docs/onboarding.md §5-6).
/// </summary>
public sealed class SeriesCardSample
{
    public required string Title { get; init; }
    public required string Name { get; init; }
    public required string Sub { get; init; }
    public int UnreadCount { get; init; }
    public bool HasUnread => UnreadCount > 0;
    public bool Missing { get; init; }
    public required IBrush CoverBrush { get; init; }

    public static IBrush Gradient(string fromHex, string toHex) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse(fromHex), 0),
            new GradientStop(Color.Parse(toHex), 1),
        },
    };
}
