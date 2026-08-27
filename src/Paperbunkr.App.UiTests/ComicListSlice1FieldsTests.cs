using FlaUI.Core.AutomationElements;

namespace Paperbunkr.App.UiTests;

/// <summary>
/// On-screen verification of the Slice 1 CE field additions to Comic List's Sort/Group (docs/
/// superpowers/specs/2026-08-18-library-book-centric-redesign-design.md) - confirms the new fields
/// render in Library's "View &amp; Sort" popup and are clickable, not just present in the field
/// catalog dictionaries. Drives the real compiled exe via FlaUI/UIA3 (see <see cref="AppFixture"/>).
/// </summary>
public class ComicListSlice1FieldsTests : IDisposable
{
    private readonly AppFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void ComicListMode_SortAndGroupPopups_OfferNewSlice1Fields()
    {
        var window = _fixture.Window;
        LibraryToolbarDriver.GoToLibrary(window);

        LibraryToolbarDriver.SelectViewMode(window, "LibraryViewModeOption_IssueList");

        LibraryToolbarDriver.SelectSort(window, "ComicListSortOption_Penciller");
        Assert.Contains("Penciller", LibraryToolbarDriver.SortChipText(window));

        LibraryToolbarDriver.SelectGroup(window, "ComicListGroupOption_Read");
        Assert.Contains("Read", LibraryToolbarDriver.GroupChipText(window));
    }
}
