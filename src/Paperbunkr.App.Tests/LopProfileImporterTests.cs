using System.Text;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Scraper.Import;
using Paperbunkr.Data;
using Paperbunkr.Data.Organizing;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="LopProfileImporter"/> against a real temp SQLite database (docs/superpowers/specs/2026-10-04-lop-profile-import-design.md): what gets saved, how names
/// are kept apart, and that an import never replaces, schedules or runs anything.
/// </summary>
public class LopProfileImporterTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pb_lop_import_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;
    private readonly OrganizerProfileStore _store;

    public LopProfileImporterTests()
    {
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using (var context = new PaperbunkrDbContext(_options))
        {
            context.Database.EnsureCreated();
        }

        _store = new OrganizerProfileStore(() => new PaperbunkrDbContext(_options));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", "Lop", name);

    private static MemoryStream Xml(string xml) => new(Encoding.UTF8.GetBytes(xml));

    private static string Profile(string name, string extra = "") =>
        $"<Profile Name=\"{name}\" Version=\"2.1\"><FolderTemplate>{{&lt;publisher&gt;}}</FolderTemplate><FileTemplate>{{&lt;series&gt;}}</FileTemplate><BaseFolder>C:\\Comics</BaseFolder><Mode>Copy</Mode><CopyMode>false</CopyMode>{extra}</Profile>";

    [Fact]
    public void ARealProfile_IsSaved_WithItsTemplatesAndRules()
    {
        var summary = LopProfileImporter.Import(_store, FixturePath("01 - Marvel-DC - Main-Limited-B&W-Annual.lop"));

        var saved = Assert.Single(summary.Saved);
        Assert.NotEqual(0, saved.Id);
        var fromStore = _store.Get(saved.Id)!;
        Assert.Equal("01 - Marvel/DC - Main/Limited/B&W/Annual", fromStore.Name);
        Assert.Contains("<Custom(First Series)>", fromStore.FolderTemplate);
        Assert.NotNull(fromStore.ExcludeRuleJson);
        Assert.False(fromStore.UseForScheduledRun);
    }

    [Fact]
    public void ImportingTheSameFileTwice_KeepsBothUnderDifferentNames()
    {
        LopProfileImporter.Import(_store, FixturePath("Events.lop"));
        LopProfileImporter.Import(_store, FixturePath("Events.lop"));
        LopProfileImporter.Import(_store, FixturePath("Events.lop"));

        Assert.Equal(["Events", "Events (2)", "Events (3)"], _store.GetAll().Select(p => p.Name).Order());
    }

    [Fact]
    public void ANameThatDiffersOnlyByCase_IsStillAClash()
    {
        _store.Save(new OrganizerProfile { Name = "events" });

        var summary = LopProfileImporter.Import(_store, FixturePath("Events.lop"));

        Assert.Equal("Events (2)", Assert.Single(summary.Saved).Name);
    }

    [Fact]
    public void TwoProfilesWithTheSameNameInOneFile_AreKeptApart()
    {
        var summary = LopProfileImporter.Import(_store, Xml($"<Profiles>{Profile("Twin")}{Profile("Twin")}</Profiles>"), "twins.lop");

        Assert.Equal(["Twin", "Twin (2)"], summary.Saved.Select(p => p.Name));
    }

    [Fact]
    public void AnExistingProfile_IsNeverReplaced()
    {
        var existing = _store.Save(new OrganizerProfile { Name = "Events", FileTemplate = "mine" });

        LopProfileImporter.Import(_store, FixturePath("Events.lop"));

        Assert.Equal("mine", _store.Get(existing.Id)!.FileTemplate);
        Assert.Equal(2, _store.GetAll().Count);
    }

    [Fact]
    public void ARefusedProfile_IsNotSaved_AndIsReported()
    {
        string bad = Profile("Bad", "<ExcludeRules Operator=\"All\" ExcludeMode=\"Only\"><ExcludeRule Field=\"Start Year\" Operator=\"is\" Value=\"2000\" /></ExcludeRules>");

        var summary = LopProfileImporter.Import(_store, Xml($"<Profiles>{bad}{Profile("Good")}</Profiles>"), "mixed.lop");

        Assert.Equal("Good", Assert.Single(summary.Saved).Name);
        Assert.Equal(1, summary.Rejected);
        Assert.Contains(summary.Notes, n => n.StartsWith("Bad was not imported:") && n.Contains("Start Year"));
        Assert.DoesNotContain(_store.GetAll(), p => p.Name == "Bad");
    }

    [Fact]
    public void WhatASavedProfileCouldNotCarryOver_IsReportedUnderItsName()
    {
        var summary = LopProfileImporter.Import(_store, FixturePath("05 - Publishers.lop"));

        Assert.Contains(summary.Notes, n => n.StartsWith("05 - Publishers: ") && n.Contains("Check the base folder"));
    }

    [Fact]
    public void ADuplicateRenamedOnImport_IsReportedUnderTheNewName()
    {
        _store.Save(new OrganizerProfile { Name = "P" });

        var summary = LopProfileImporter.Import(_store, Xml(Profile("P", "<Prefix><Item Name=\"series\" Value=\"[\" /></Prefix>")), "p.lop");

        Assert.Contains(summary.Notes, n => n.StartsWith("P (2): "));
    }

    [Fact]
    public void AnUnreadableFile_IsAFailure_AndSavesNothing()
    {
        var summary = LopProfileImporter.Import(_store, Xml("not xml"), "junk.lop");

        Assert.True(summary.Failed);
        Assert.Empty(summary.Saved);
        Assert.StartsWith("junk.lop: ", Assert.Single(summary.Notes));
        Assert.Empty(_store.GetAll());
    }

    [Fact]
    public void AMissingFile_IsAFailure_NotAnException()
    {
        var summary = LopProfileImporter.Import(_store, Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid() + ".lop"));

        Assert.True(summary.Failed);
        Assert.Empty(_store.GetAll());
    }

    [Fact]
    public void ANewProfile_NeverBecomesTheScheduledOne_EvenIfTheFileSaysSo()
    {
        var summary = LopProfileImporter.Import(_store, Xml(Profile("P", "<UseForScheduledRun>true</UseForScheduledRun>")), "p.lop");

        Assert.False(Assert.Single(summary.Saved).UseForScheduledRun);
    }
}
