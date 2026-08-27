using FlaUI.Core.AutomationElements;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.UiTests;

/// <summary>
/// On-screen verification of the configurable Details table (docs/superpowers/specs/2026-08-27-
/// library-browsing-4b-toolbar-rework-design.md §8): the header renders click-to-sort cells and a
/// click sorts / flips direction. Column add/remove + relaunch persistence stay in the manual
/// checklist (FlaUI right-click + restart is flaky in this env). Drives the real compiled exe via
/// FlaUI/UIA3 (see <see cref="AppFixture"/>).
/// </summary>
public class LibraryDetailsColumnsTests : IDisposable
{
    private readonly AppFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void DetailsHeader_ClickSortsByColumn_ClickAgainFlipsDirection()
    {
        SeedTwoIssues();

        var window = _fixture.Window;
        LibraryToolbarDriver.GoToLibrary(window);
        LibraryToolbarDriver.SelectViewMode(window, "LibraryViewModeOption_Details");

        // The default column set includes Year; its header cell is a real click-to-sort control.
        var yearHeader = LibraryToolbarDriver.Find(window, "LibraryDetailsHeader_Year");
        yearHeader.AsButton().Invoke();
        Assert.Contains("Year", LibraryToolbarDriver.SortChipText(window));

        var chipAfterFirst = LibraryToolbarDriver.SortChipText(window);
        LibraryToolbarDriver.Find(window, "LibraryDetailsHeader_Year").AsButton().Invoke();
        // Same field, flipped direction - the chip's arrow glyph changes.
        Assert.NotEqual(chipAfterFirst, LibraryToolbarDriver.SortChipText(window));
    }

    private void SeedTwoIssues()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>()
            .UseSqlite($"Data Source={_fixture.DbPath}").Options;
        using var context = new PaperbunkrDbContext(options);
        var series = new Series { Name = "Details Table Series" };
        context.Series.Add(series);
        context.SaveChanges();
        context.Issues.Add(new Issue { SeriesId = series.Id, Number = "1", Year = 2001 });
        context.Issues.Add(new Issue { SeriesId = series.Id, Number = "2", Year = 2020 });
        context.SaveChanges();
    }
}
