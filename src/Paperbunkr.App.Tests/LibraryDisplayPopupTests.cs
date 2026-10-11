using System;
using System.IO;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The Library toolbar after the 2026-10-04 audit of the old three-tab "View &amp; Sort" popup: a Display popup on the row-1
/// button, Sort and Group popups on their row-2 chips, one Language row over the two stored settings, the progress-bar switch
/// shared with Preferences, and the three behaviour switches that moved to Preferences. View-model level.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class LibraryDisplayPopupTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;
    private readonly bool _originalProgressRing;

    public LibraryDisplayPopupTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _originalProgressRing = CosmeticThumbnailSettings.ProgressRing;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_library_display_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        CosmeticThumbnailSettings.ProgressRing = _originalProgressRing;
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private static LibraryScreenViewModel CreateVm() =>
        new(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

    private static AppSettings ReadSettings()
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.GetOrCreateAppSettings();
    }

    [Fact]
    public void DisplaySortAndGroup_AreSeparatePopups_OnlyOneOpenAtATime()
    {
        var vm = CreateVm();

        vm.ToggleDisplayCommand.Execute(null);
        Assert.True(vm.IsDisplayOpen);

        vm.ToggleSortCommand.Execute(null);
        Assert.True(vm.IsSortOpen);
        Assert.False(vm.IsDisplayOpen);

        vm.ToggleGroupCommand.Execute(null);
        Assert.True(vm.IsGroupOpen);
        Assert.False(vm.IsSortOpen);

        vm.ToggleGroupCommand.Execute(null);
        Assert.False(vm.IsGroupOpen);
    }

    [Fact]
    public void GroupChip_ReadsNone_WhenUngrouped()
    {
        var vm = CreateVm();
        vm.IssueList.SetGroupFieldCommand.Execute(IssueListGroupField.None);

        Assert.Equal("None", vm.ActiveGroupLabel);
    }

    [Fact]
    public void LanguageRow_MapsOffTextFlag_OntoTheTwoStoredSettings()
    {
        var vm = CreateVm();
        Assert.True(vm.IsLanguageOff);

        vm.SetLanguageDisplayCommand.Execute("Flag");
        Assert.True(vm.IsLanguageFlag);
        Assert.True(ReadSettings().LibraryShowLanguageBadge);
        Assert.True(ReadSettings().LibraryUseLanguageIcon);

        vm.SetLanguageDisplayCommand.Execute("Text");
        Assert.True(vm.IsLanguageText);
        Assert.False(vm.IsLanguageFlag);
        Assert.False(ReadSettings().LibraryUseLanguageIcon);

        // Off keeps the text-or-flag choice.
        vm.SetLanguageDisplayCommand.Execute("Flag");
        vm.SetLanguageDisplayCommand.Execute("Off");
        Assert.True(vm.IsLanguageOff);
        Assert.False(ReadSettings().LibraryShowLanguageBadge);
        Assert.True(ReadSettings().LibraryUseLanguageIcon);
    }

    [Fact]
    public void ProgressBarSwitch_IsThePreferencesSetting()
    {
        var vm = CreateVm();
        Assert.True(vm.ShowProgressBar);

        vm.ShowProgressBar = false;

        Assert.False(CosmeticThumbnailSettings.ProgressRing);
        Assert.False(ReadSettings().ProgressRing);

        // A change made in Preferences shows when the popup next opens.
        string? raised = null;
        vm.PropertyChanged += (_, e) => raised = e.PropertyName == nameof(vm.ShowProgressBar) ? e.PropertyName : raised;
        CosmeticThumbnailSettings.ProgressRing = true;
        vm.ToggleDisplayCommand.Execute(null);
        Assert.Equal(nameof(vm.ShowProgressBar), raised);
        Assert.True(vm.ShowProgressBar);
    }

    /// <summary>The Library used to write Fade in, Tooltips and Smooth scrolling from its own copy on every settings save. They
    /// are Preferences settings now, so a Library save must leave what Preferences wrote alone.</summary>
    [Fact]
    public void LibrarySave_LeavesThePreferencesOwnedSwitchesAlone()
    {
        var vm = CreateVm();
        using (var context = PaperbunkrDb.CreateContext())
        {
            var settings = context.GetOrCreateAppSettings();
            settings.FadeInThumbnails = false;
            settings.ShowToolTips = true;
            settings.SmoothScrolling = false;
            context.SaveChanges();
        }

        vm.ShowUnreadBadge = !vm.ShowUnreadBadge;   // any Library setting change saves the lot

        var after = ReadSettings();
        Assert.False(after.FadeInThumbnails);
        Assert.True(after.ShowToolTips);
        Assert.False(after.SmoothScrolling);
    }
}
