namespace Paperbunkr.Data.Entities;

/// <summary>
/// Per-page type tagging (docs/ce-feature-inventory.md §A "Per-page type tagging"). A deliberately
/// simplified subset of CE's real <c>ComicPageType</c> flags enum (<c>_reference/ComicRackCE/
/// ComicRack.Engine/ComicPageType.cs</c>: FrontCover/InnerCover/Roundup/Story/Advertisement/
/// Editorial/Letters/Preview/BackCover/Other/Deleted) - Paperbunkr has no reading-filter mechanism
/// that would ever act on the finer distinctions (InnerCover vs. Roundup vs. Editorial vs. Letters
/// vs. Preview all reduce to "not a story page" here), so this only keeps the four the inventory
/// doc already scoped: <c>Story</c> is the default and never shown as a badge; the other three mark
/// a page as something other than story content.
/// </summary>
public enum PageType
{
    Story = 0,
    Cover = 1,
    Advertisement = 2,
    Deleted = 3,
}
