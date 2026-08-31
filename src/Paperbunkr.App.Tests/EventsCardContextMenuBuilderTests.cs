using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="EventsCardContextMenuBuilder"/> (docs/superpowers/specs/2026-08-31-
/// keyboard-operability-design.md) - a new menu for the Events &amp; Continuity sidebar rows, which
/// had none before. Lives on <see cref="MainViewModel"/> (see the builder's own doc comment for why).
/// Same DB-redirect fixture shape as <see cref="MainViewModelTests"/>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class EventsCardContextMenuBuilderTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public EventsCardContextMenuBuilderTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_eventsctx_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
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

    [Fact]
    public void Build_EventRow_ReturnsOpenEditDeleteEntries()
    {
        var vm = new MainViewModel();
        var builder = new EventsCardContextMenuBuilder(vm);
        var row = new StoryEventSummary { Id = 1, Name = "Event", DeleteConfirm = new TwoStepConfirm(() => { }) };

        var entries = builder.Build(row);

        Assert.NotNull(entries);
        var headers = entries!.Select(e => e.IsSeparator ? null : e.Header).ToList();
        Assert.Equal(new[] { "Open", "Edit details", null, "Remove" }, headers);
        Assert.Same(vm.Events.SelectEventCommand, entries[0].Command);
        Assert.Same(row, entries[0].CommandParameter);
        Assert.Same(vm.EditEventFromContextMenuCommand, entries[1].Command);
    }

    [Fact]
    public void Build_ContinuityRow_WithDeleteConfirm_ReturnsOpenEditDeleteEntries()
    {
        var vm = new MainViewModel();
        var builder = new EventsCardContextMenuBuilder(vm);
        var row = new ContinuitySummary(1, "Continuity", null, 0) { DeleteConfirm = new TwoStepConfirm(() => { }) };

        var entries = builder.Build(row);

        Assert.NotNull(entries);
        var headers = entries!.Select(e => e.IsSeparator ? null : e.Header).ToList();
        Assert.Equal(new[] { "Open", "Edit details", null, "Remove" }, headers);
    }

    [Fact]
    public void Build_ContinuityRow_WithoutDeleteConfirm_OmitsDeleteEntry()
    {
        var vm = new MainViewModel();
        var builder = new EventsCardContextMenuBuilder(vm);
        var row = new ContinuitySummary(1, "Picker Continuity", null, 0);

        var entries = builder.Build(row);

        Assert.NotNull(entries);
        Assert.Equal(new[] { "Open", "Edit details" }, entries!.Select(e => e.Header));
    }

    [Fact]
    public void Build_UnrecognizedTarget_ReturnsNull()
    {
        var builder = new EventsCardContextMenuBuilder(new MainViewModel());

        Assert.Null(builder.Build(new object()));
        Assert.Null(builder.Build(null));
    }
}
