using System;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>The New event / New continuity dialog (docs/superpowers/specs/2026-08-28-events-
/// continuity-screen-redesign-design.md). Temp-DB pattern, same as <see cref="ContinuityScreen.ContinuityScreenTestBase"/>.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class NewEventOrContinuityViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public NewEventOrContinuityViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"pbnec-{Guid.NewGuid():N}.db");
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.Migrate();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Event_NameOnly_Creates_AndFiresCallback()
    {
        NewEventOrContinuityViewModel.Kind kind = default;
        int created = 0;
        var vm = new NewEventOrContinuityViewModel((k, id) => { kind = k; created = id; }, () => { });
        vm.Reset(NewEventOrContinuityViewModel.Kind.Event);
        vm.Name = "Secret Invasion";

        Assert.True(vm.CanCreate);
        vm.CreateCommand.Execute(null);

        Assert.Equal(NewEventOrContinuityViewModel.Kind.Event, kind);
        Assert.NotEqual(0, created);
        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal("Secret Invasion", context.StoryEvents.Single(e => e.Id == created).Name);
    }

    [Fact]
    public void Continuity_CarriesPublisher()
    {
        int created = 0;
        var vm = new NewEventOrContinuityViewModel((_, id) => created = id, () => { });
        vm.Reset(NewEventOrContinuityViewModel.Kind.Continuity);
        vm.Name = "Ultimate Universe";
        vm.Publisher = "Marvel";

        vm.CreateCommand.Execute(null);

        using var context = PaperbunkrDb.CreateContext();
        var c = context.Continuities.Single(x => x.Id == created);
        Assert.Equal("Ultimate Universe", c.Name);
        Assert.Equal("Marvel", c.Publisher);
    }

    /// <summary>Carries the old sidebar "new continuity" test's intent (docs/superpowers/specs/2026-09-28-continuity-screen-redesign-design.md):
    /// a name that already exists, in any case, opens that continuity instead of making a second one.</summary>
    [Fact]
    public void NewContinuity_WithAnExistingName_ReusesIt_CaseInsensitively()
    {
        var ids = new List<int>();
        var vm = new NewEventOrContinuityViewModel((_, id) => ids.Add(id), () => { });
        vm.Reset(NewEventOrContinuityViewModel.Kind.Continuity);
        vm.Name = "Earth-616";
        vm.CreateCommand.Execute(null);
        vm.Reset(NewEventOrContinuityViewModel.Kind.Continuity);
        vm.Name = "earth-616";
        vm.Publisher = "Marvel";
        vm.CreateCommand.Execute(null);

        Assert.Equal(ids[0], ids[1]);
        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal("Marvel", Assert.Single(context.Continuities).Publisher);     // filled in what it lacked
    }

    [Fact]
    public void ContinuityDialog_SuggestsTheLibrarysPublishers()
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            var series = new Series { Name = "X-Men" };
            series.Issues.Add(new Issue { Number = "1", Publisher = "Marvel" });
            series.Issues.Add(new Issue { Number = "2", Publisher = "marvel" });
            context.Series.Add(series);
            context.Continuities.Add(new Continuity { Name = "Prime", Publisher = "DC Comics", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            context.SaveChanges();
        }

        var vm = new NewEventOrContinuityViewModel((_, _) => { }, () => { });
        vm.Reset(NewEventOrContinuityViewModel.Kind.Continuity);

        Assert.Equal(2, vm.PublisherSuggestions.Count);                        // "Marvel" and "marvel" are one publisher
        Assert.Equal("DC Comics", vm.PublisherSuggestions[0]);
        Assert.Equal("marvel", vm.PublisherSuggestions[1], ignoreCase: true);
        Assert.False(vm.HasPublisher);
        vm.Publisher = "Marvel";
        Assert.True(vm.HasPublisher);
    }

    [Fact]
    public void CanCreate_FalseForBlankName()
    {
        var vm = new NewEventOrContinuityViewModel((_, _) => { }, () => { });
        vm.Reset(NewEventOrContinuityViewModel.Kind.Event);
        vm.Name = "   ";
        Assert.False(vm.CanCreate);
    }

    [Fact]
    public void LoadForEdit_Continuity_PrefillsAndUpdatesInPlace()
    {
        int continuityId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var c = new Continuity { Name = "Old Name", Publisher = "DC", Description = "old desc", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            context.Continuities.Add(c);
            context.SaveChanges();
            continuityId = c.Id;
        }

        int savedId = 0;
        var vm = new NewEventOrContinuityViewModel((_, id) => savedId = id, () => { });
        vm.LoadForEdit(NewEventOrContinuityViewModel.Kind.Continuity, continuityId);

        Assert.True(vm.IsEdit);
        Assert.Equal("Old Name", vm.Name);
        Assert.Equal("DC", vm.Publisher);
        Assert.Equal("old desc", vm.Description);
        Assert.Equal("EDIT CONTINUITY", vm.Title);
        Assert.Equal("Save", vm.SaveButtonLabel);

        vm.Name = "New Name";
        vm.Description = "new desc";
        vm.CreateCommand.Execute(null);

        Assert.Equal(continuityId, savedId);
        using var check = PaperbunkrDb.CreateContext();
        var updated = check.Continuities.Single(x => x.Id == continuityId);
        Assert.Equal("New Name", updated.Name);
        Assert.Equal("new desc", updated.Description);
        Assert.Single(check.Continuities);
    }

    [Fact]
    public void LoadForEdit_Event_UpdatesNameAndDescription()
    {
        int eventId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var e = new StoryEvent { Name = "Draft", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            context.StoryEvents.Add(e);
            context.SaveChanges();
            eventId = e.Id;
        }

        var vm = new NewEventOrContinuityViewModel((_, _) => { }, () => { });
        vm.LoadForEdit(NewEventOrContinuityViewModel.Kind.Event, eventId);
        vm.Name = "Final Title";
        vm.Description = "what happens";
        vm.CreateCommand.Execute(null);

        using var context2 = PaperbunkrDb.CreateContext();
        var updated = context2.StoryEvents.Single(e => e.Id == eventId);
        Assert.Equal("Final Title", updated.Name);
        Assert.Equal("what happens", updated.Description);
        Assert.Single(context2.StoryEvents);
    }
}
