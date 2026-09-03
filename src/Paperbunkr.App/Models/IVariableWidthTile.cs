namespace Paperbunkr.App.Models;

/// <summary>
/// A grid item that knows its own preferred pixel width, so <c>VirtualizingVariableWrapPanel</c>
/// can lay out (and virtualize) a wrapping flow of differently-sized tiles from the data alone -
/// without realizing every container to measure it. Only Panorama's cover tiles implement this;
/// every other Library/Books grid mode is uniform-cell and rides <c>VirtualizingWrapPanel</c>.
/// </summary>
public interface IVariableWidthTile
{
    /// <summary>Target width for this tile, in DIPs. The panel packs rows against this and the
    /// item's DataTemplate should render at the same width (it binds the same source value).</summary>
    double PreferredWidth { get; }
}
