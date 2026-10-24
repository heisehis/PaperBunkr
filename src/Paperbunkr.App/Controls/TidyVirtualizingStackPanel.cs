using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Paperbunkr.App.Controls;

/// <summary>
/// A <see cref="VirtualizingStackPanel"/> that never leaves a stray row on screen after <c>ScrollIntoView</c>.
///
/// Avalonia's panel (checked against the 12.1.1 source) scrolls to an unrealized index by creating that row's container early, adding it
/// as a child, arranging it at an <em>estimated</em> position, and then running layout so the normal measure pass adopts it. When that
/// pass does not realize the index after all (the estimate was off because rows differ in height, so the real viewport ends up elsewhere),
/// the panel simply forgets the container: it is neither realized nor recycled, so it stays a visible child at the estimated position,
/// still showing its old item, drawn over whatever rows really belong there. It also survives the list being cleared and refilled.
/// Seen on the reading-list path, where the tall "Up next" card is exactly the row that gets scrolled to: a leftover card from the
/// previously opened list sat on top of two rows of the next one.
///
/// This removes such a container as soon as <c>ScrollIntoView</c> returns.
/// </summary>
public class TidyVirtualizingStackPanel : VirtualizingStackPanel
{
    protected override Control? ScrollIntoView(int index)
    {
        var element = base.ScrollIntoView(index);
        if (element is null || IndexFromContainer(element) >= 0 || !ReferenceEquals(element.GetVisualParent(), this))
        {
            return element;
        }

        // Created for the scroll, then dropped by the layout pass: nothing tracks it any more.
        ItemsControl?.ItemContainerGenerator.ClearItemContainer(element);
        RemoveInternalChild(element);
        return null;
    }
}
