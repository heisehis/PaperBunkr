using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="NeedsReviewViewModel"/>'s "Metadata Proposals" section (docs/superpowers/
/// specs/2026-08-17-metadata-model-phase2a-metadata-proposals-design.md) - the newest of its four
/// sections. Redirects <see cref="PaperbunkrDbContext.DatabasePathOverride"/> to a temp SQLite
/// file, same approach as <see cref="DetailScreenViewModelTests"/> since <see cref="NeedsReviewViewModel"/>
/// has no injected context-factory seam of its own; joins <see cref="AvaloniaTestCollection"/> for
/// the same reason.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class NeedsReviewViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;
    private readonly int _issueId;

    public NeedsReviewViewModelTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_needsreviewvm_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(options);
        context.Database.EnsureCreated();

        // ContentType.Comic (not the default Unknown) - keeps this seeded series out of the
        // unrelated "Content Type" Needs Review section, so HasPendingItems tests below reflect
        // only the Metadata Proposals section under test.
        var series = new Series { Name = "Kilo Station", ContentType = ContentType.Comic };
        context.Series.Add(series);
        var issue = new Issue { Series = series, Number = null };
        context.Issues.Add(issue);
        context.SaveChanges();
        _issueId = issue.Id;
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

    private static NeedsReviewViewModel CreateViewModel() => new(onOpenSeriesDetail: _ => { }, filePicker: new NoOpFilePicker());

    private void AddProposal(MetadataProposalField field, string proposedValue, MetadataProposalStatus status)
    {
        using var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        context.MetadataProposals.Add(new MetadataProposal
        {
            IssueId = _issueId,
            Field = field,
            ProposedValue = proposedValue,
            Source = MetadataProposalSource.FilenameParser,
            Confidence = 0.6m,
            Status = status,
        });
        context.SaveChanges();
    }

    [Fact]
    public void Refresh_NoProposals_SectionEmpty()
    {
        var vm = CreateViewModel();

        Assert.False(vm.HasMetadataProposalItems);
        Assert.Empty(vm.MetadataProposalItems);
    }

    [Fact]
    public void Refresh_PendingProposal_AppearsInSection()
    {
        AddProposal(MetadataProposalField.Number, "12", MetadataProposalStatus.Pending);

        var vm = CreateViewModel();

        Assert.True(vm.HasMetadataProposalItems);
        var row = Assert.Single(vm.MetadataProposalItems);
        Assert.Equal("Number", row.FieldLabel);
        Assert.Equal("12", row.ProposedValue);
        Assert.False(row.IsAlreadyAccepted);
        Assert.False(row.IsResolved);
    }

    [Fact]
    public void Refresh_AcceptedProposal_StillAppears_MarkedAlreadyAccepted()
    {
        AddProposal(MetadataProposalField.Number, "12", MetadataProposalStatus.Accepted);

        var vm = CreateViewModel();

        Assert.True(vm.HasMetadataProposalItems);
        var row = Assert.Single(vm.MetadataProposalItems);
        Assert.True(row.IsAlreadyAccepted);
        Assert.False(row.IsResolved); // still actionable, not "done"
    }

    [Fact]
    public void Refresh_RejectedProposal_DoesNotAppear()
    {
        AddProposal(MetadataProposalField.Number, "12", MetadataProposalStatus.Rejected);

        var vm = CreateViewModel();

        Assert.False(vm.HasMetadataProposalItems);
        Assert.Empty(vm.MetadataProposalItems);
    }

    [Fact]
    public void AcceptCommand_PendingProposal_UpdatesStatusInDatabase_AndRowState()
    {
        AddProposal(MetadataProposalField.Number, "12", MetadataProposalStatus.Pending);
        var vm = CreateViewModel();
        var row = Assert.Single(vm.MetadataProposalItems);

        row.AcceptCommand.Execute(null);

        Assert.True(row.IsResolved);
        Assert.Equal("Accepted", row.ResolutionLabel);

        using var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        var proposal = Assert.Single(context.MetadataProposals);
        Assert.Equal(MetadataProposalStatus.Accepted, proposal.Status);
        Assert.NotNull(proposal.ResolvedAt);
    }

    [Fact]
    public void RejectCommand_AcceptedProposal_UpdatesStatusInDatabase_AndRowState()
    {
        AddProposal(MetadataProposalField.Number, "12", MetadataProposalStatus.Accepted);
        var vm = CreateViewModel();
        var row = Assert.Single(vm.MetadataProposalItems);

        row.RejectCommand.Execute(null);

        Assert.True(row.IsResolved);
        Assert.Equal("Rejected", row.ResolutionLabel);

        using var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        var proposal = Assert.Single(context.MetadataProposals);
        Assert.Equal(MetadataProposalStatus.Rejected, proposal.Status);
    }

    [Fact]
    public void HasPendingItems_ReflectsMetadataProposalSection()
    {
        Assert.False(CreateViewModel().HasPendingItems);

        AddProposal(MetadataProposalField.Number, "12", MetadataProposalStatus.Pending);

        Assert.True(CreateViewModel().HasPendingItems);
    }

    // --- Series-field proposals (docs/superpowers/specs/2026-08-17-metadata-model-phase2b-series-
    // reassignment-design.md) - write-time, unlike every other field above ---

    [Fact]
    public void AcceptCommand_SeriesProposal_ActuallyMovesTheIssue_NotJustFlipsStatus()
    {
        AddProposal(MetadataProposalField.Series, "Renamed Series", MetadataProposalStatus.Pending);
        var vm = CreateViewModel();
        var row = Assert.Single(vm.MetadataProposalItems);

        row.AcceptCommand.Execute(null);

        using var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        var issue = context.Issues.Include(i => i.Series).Single(i => i.Id == _issueId);
        Assert.Equal("Renamed Series", issue.Series!.Name);
        // The original "Kilo Station" series had only this one issue - it's gone now.
        Assert.DoesNotContain(context.Series, s => s.Name == "Kilo Station");
    }

    [Fact]
    public void RejectCommand_SeriesProposal_LeavesIssueOnItsOriginalSeries()
    {
        AddProposal(MetadataProposalField.Series, "Renamed Series", MetadataProposalStatus.Pending);
        var vm = CreateViewModel();
        var row = Assert.Single(vm.MetadataProposalItems);

        row.RejectCommand.Execute(null);

        using var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        var issue = context.Issues.Include(i => i.Series).Single(i => i.Id == _issueId);
        Assert.Equal("Kilo Station", issue.Series!.Name);
        Assert.DoesNotContain(context.Series, s => s.Name == "Renamed Series");
    }

    // --- Series-scoped Summary/Status/Genre proposals (docs/superpowers/specs/2026-08-23-apply-
    // from-provider-design.md) - written directly on accept, unlike every Issue-scoped field above,
    // so Reject needs a real revert step rather than just a status flip. ---

    private int AddSeriesProposal(MetadataProposalField field, string? currentValue, string proposedValue)
    {
        using var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        int seriesId = context.Issues.Single(i => i.Id == _issueId).SeriesId;
        var proposal = new MetadataProposal
        {
            SeriesId = seriesId,
            Field = field,
            CurrentValue = currentValue,
            ProposedValue = proposedValue,
            Source = MetadataProposalSource.MetadataProvider,
            ProviderKey = ExternalMetadataProvider.MangaBaka,
            Confidence = 1.0m,
            Status = MetadataProposalStatus.Accepted,
            ResolvedAt = DateTime.UtcNow,
        };
        context.MetadataProposals.Add(proposal);
        context.SaveChanges();
        return proposal.Id;
    }

    [Fact]
    public void Refresh_SeriesScopedProposal_LabelIsJustTheSeriesName()
    {
        AddSeriesProposal(MetadataProposalField.Summary, null, "A synopsis.");

        var vm = CreateViewModel();

        var row = Assert.Single(vm.MetadataProposalItems);
        Assert.Equal("Kilo Station", row.IssueLabel);
        Assert.True(row.IsAlreadyAccepted);
    }

    [Fact]
    public void RejectCommand_SeriesScopedProposal_RevertsFieldToSnapshottedCurrentValue()
    {
        using (var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options))
        {
            var series = context.Series.Single();
            series.Summary = "Provider-written summary.";
            context.SaveChanges();
        }

        AddSeriesProposal(MetadataProposalField.Summary, currentValue: "My own hand-written summary.", proposedValue: "Provider-written summary.");
        var vm = CreateViewModel();
        var row = Assert.Single(vm.MetadataProposalItems);

        row.RejectCommand.Execute(null);

        using var verifyContext = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        Assert.Equal("My own hand-written summary.", verifyContext.Series.Single().Summary);
        Assert.Equal(MetadataProposalStatus.Rejected, verifyContext.MetadataProposals.Single().Status);
    }

    [Fact]
    public void RejectCommand_SeriesScopedStatusProposal_RevertsToParsedPriorEnumValue()
    {
        using (var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options))
        {
            var series = context.Series.Single();
            series.Status = SeriesStatus.Ongoing;
            context.SaveChanges();
        }

        AddSeriesProposal(MetadataProposalField.Status, currentValue: "Completed", proposedValue: "Ongoing");
        var vm = CreateViewModel();
        var row = Assert.Single(vm.MetadataProposalItems);

        row.RejectCommand.Execute(null);

        using var verifyContext = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        Assert.Equal(SeriesStatus.Completed, verifyContext.Series.Single().Status);
    }

    private sealed class NoOpFilePicker : IFilePickerService
    {
        public Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel) => Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel) => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

        public Task SetClipboardTextAsync(string text) => Task.CompletedTask;
    }
}
