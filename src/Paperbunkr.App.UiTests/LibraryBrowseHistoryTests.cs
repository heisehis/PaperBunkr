using FlaUI.Core.AutomationElements;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.UiTests;

/// <summary>
/// On-screen verification of Library's browse history (docs/superpowers/specs/2026-08-19-library-
/// browse-history-design.md) - confirms the two toolbar buttons exist, start disabled, a sidebar
/// click enables Back, and clicking Back actually returns to the prior sidebar selection on screen.
/// Push/truncate/debounce logic itself is covered by <c>LibraryScreenViewModelTests</c>. Drives the
/// real compiled exe via FlaUI/UIA3 (see <see cref="AppFixture"/>).
/// </summary>
public class LibraryBrowseHistoryTests : IDisposable
{
    private readonly AppFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void BackButton_StartsDisabled_ThenEnabledAfterASidebarClick_AndReturnsToAllSeriesWhenClicked()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_fixture.DbPath}").Options;
        using (var context = new PaperbunkrDbContext(options))
        {
            var comic = new Series { Name = "Comic Series", ContentType = ContentType.Comic };
            var manga = new Series { Name = "Manga Series", ContentType = ContentType.Manga };
            context.Series.AddRange(comic, manga);
            context.SaveChanges();
        }

        var window = _fixture.Window;
        window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryRailButton"))!.AsButton().Invoke();

        var backButton = window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryBrowsePreviousButton"))!.AsButton();
        var forwardButton = window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryBrowseNextButton"))!.AsButton();
        Assert.False(backButton.IsEnabled);
        Assert.False(forwardButton.IsEnabled);

        window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryContentTypeOption_Manga"))!.AsButton().Invoke();
        Assert.True(backButton.IsEnabled);

        backButton.Invoke();

        var allSeries = window.FindFirstDescendant(cf => cf.ByAutomationId("LibrarySidebarAllSeries"))!;
        Assert.Equal("True", allSeries.Properties.HelpText.Value);
        Assert.False(backButton.IsEnabled);
        Assert.True(forwardButton.IsEnabled);
    }
}
