using System;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Services;

/// <summary>
/// Whether a collection's stored icon can still be drawn (docs/superpowers/specs/2026-11-06-collection-icons-and-slim-banner-design.md). A publisher whose logo is gone or
/// a "My icons" file that was deleted resolves to null, and every surface then shows the accent dot, like an unknown symbol always has.
/// </summary>
public static class CollectionIconResolver
{
    /// <summary>The icon <paramref name="stored"/> names, or null when it is empty, unknown or can no longer be drawn.</summary>
    public static CollectionIcon? Resolve(string? stored, string? folder = null)
    {
        var icon = CollectionIcon.Parse(stored);
        return icon is not null && CanDraw(icon, folder) ? icon : null;
    }

    public static bool CanDraw(CollectionIcon icon, string? folder = null) => icon.Kind switch
    {
        CollectionIconKind.Publisher => HasPublisherLogo(icon.Key),
        CollectionIconKind.File => System.IO.File.Exists(System.IO.Path.Combine(folder ?? CollectionIconFolder.Path, icon.Key)),
        _ => true,
    };

    /// <summary>True when the publisher has a real logo (an SVG or image), not just its name or initials.</summary>
    public static bool HasPublisherLogo(string? publisher)
    {
        if (string.IsNullOrWhiteSpace(publisher))
        {
            return false;
        }

        try
        {
            return MarkResolver.Instance.ResolvePublisher(publisher).Kind is MarkKind.SvgAsset or MarkKind.Raster;
        }
        catch (Exception)
        {
            // The resolver reads bundled assets; without a running app (some unit tests) there is no logo to draw.
            return false;
        }
    }
}
