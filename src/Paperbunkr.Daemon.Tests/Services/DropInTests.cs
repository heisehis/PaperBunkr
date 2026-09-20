using Paperbunkr.Daemon.Services;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Daemon.Tests.Services;

/// <summary>A file the user added by hand closes the matching want (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md §5).</summary>
public class DropInTests : CycleTestBase
{
    private readonly FakeDownloadClient _client = new();

    private int LinkSeries()
    {
        using var context = NewContext();
        var series = new Series { Name = "Spawn" };
        context.Series.Add(series);
        context.SaveChanges();
        context.WatchedSeries.Single().SeriesId = series.Id;
        context.SaveChanges();
        return series.Id;
    }

    private void Own(int seriesId, string number, bool placeholder = false, bool missing = false)
    {
        using var context = NewContext();
        context.Issues.Add(new Issue { SeriesId = seriesId, Number = number, FilePath = $"C:\\x\\{number}.cbz", IsPlaceholder = placeholder, FileIsMissing = missing });
        context.SaveChanges();
    }

    [Fact]
    public void CloseOwned_ClosesWhatTheLibraryNowHas_AndLeavesTheRest()
    {
        var ids = AddWanted((DateTime?)null, (DateTime?)null, (DateTime?)null, (DateTime?)null);   // #261 #262 #263 #264
        var seriesId = LinkSeries();
        Own(seriesId, "261");
        Own(seriesId, "0262");                                   // padding differences don't matter
        Own(seriesId, "263", placeholder: true);                 // a placeholder is not owned
        using (var context = NewContext())
        {
            context.ReleaseCandidates.Add(new ReleaseCandidate { WantedIssueId = ids[0], Title = "x", DownloadUrl = "magnet:x", FoundAt = Now });
            context.WantedIssues.Single(w => w.Id == ids[3]).Status = WantedIssueStatus.Ignored;
            context.SaveChanges();
        }

        using var check = NewContext();
        var inFlight = WantedService.CloseOwned(check);

        Assert.Empty(inFlight);
        var rows = check.WantedIssues.ToDictionary(w => w.Id);
        Assert.Equal(WantedIssueStatus.Imported, rows[ids[0]].Status);
        Assert.NotNull(rows[ids[0]].IssueId);
        Assert.Equal(WantedIssueStatus.Imported, rows[ids[1]].Status);
        Assert.Equal(WantedIssueStatus.Wanted, rows[ids[2]].Status);      // placeholder only
        Assert.Equal(WantedIssueStatus.Ignored, rows[ids[3]].Status);     // untouched
        Assert.Empty(check.ReleaseCandidates);
    }

    [Fact]
    public void CloseOwned_ReturnsTheInFlightRows_SoTheirTorrentCanBeDropped()
    {
        var ids = AddWanted((DateTime?)null);
        var seriesId = LinkSeries();
        Own(seriesId, "261");
        using (var context = NewContext())
        {
            var w = context.WantedIssues.Single();
            w.Status = WantedIssueStatus.Downloading; w.TorrentHash = FakeDownloadClient.Hash('a');
            context.SaveChanges();
        }

        using var check = NewContext();
        var inFlight = WantedService.CloseOwned(check);

        Assert.Equal(ids[0], Assert.Single(inFlight).Id);
        Assert.Equal(WantedIssueStatus.Imported, check.WantedIssues.Single().Status);
    }

    [Fact]
    public void CloseOwned_DoesNothing_ForASeriesNotLinkedToALocalOne()
    {
        AddWanted((DateTime?)null);                              // WatchedSeries.SeriesId stays null: nothing to compare against

        using var context = NewContext();
        Assert.Empty(WantedService.CloseOwned(context));
        Assert.Equal(WantedIssueStatus.Wanted, context.WantedIssues.Single().Status);
    }

    [Fact]
    public async Task ACycle_ClosesOwnedIssues_BeforeSearching_AndDropsAnUnfinishedDuplicateTorrent()
    {
        Configure();
        AddWanted((DateTime?)null, (DateTime?)null);
        var seriesId = LinkSeries();
        Own(seriesId, "261");
        using (var context = NewContext())
        {
            var w = context.WantedIssues.OrderBy(x => x.Id).First();
            w.Status = WantedIssueStatus.Downloading; w.TorrentHash = FakeDownloadClient.Hash('a');
            context.SaveChanges();
        }

        _client.Statuses[FakeDownloadClient.Hash('a')] = FakeDownloadClient.Status('a', 0.3);
        var cycle = new AcquisitionCycle(NewContext, (_, _) => Indexer, _ => ComicVine, Events, () => Now, new GrabService(NewContext, _ => _client, Events));

        await cycle.RunAsync(manual: true, CancellationToken.None);

        Assert.Equal(new[] { (FakeDownloadClient.Hash('a'), true) }, _client.Removed);          // the unfinished duplicate is gone, with its partial files
        Assert.DoesNotContain(Indexer.Queries, q => q.Contains("261"));                          // and #261 was never searched for
        Assert.Contains(Indexer.Queries, q => q.Contains("262"));                                // #262 still was
    }

    [Fact]
    public async Task AFinishedDuplicateTorrent_IsLeftAlone()
    {
        var ids = AddWanted((DateTime?)null);
        var seriesId = LinkSeries();
        Own(seriesId, "261");
        using (var context = NewContext())
        {
            var w = context.WantedIssues.Single();
            w.Status = WantedIssueStatus.Downloading; w.TorrentHash = FakeDownloadClient.Hash('a');
            context.SaveChanges();
        }

        _client.Statuses[FakeDownloadClient.Hash('a')] = FakeDownloadClient.Status('a', 1.0, Paperbunkr.Daemon.Clients.DownloadState.Completed);

        using (var context = NewContext())
        {
            foreach (var closed in WantedService.CloseOwned(context))
            {
                await new GrabService(NewContext, _ => _client, Events).DiscardIfIncompleteAsync(closed.TorrentHash!, CancellationToken.None);
            }
        }

        Assert.Empty(_client.Removed);                            // a completed torrent is a good seeding file: not removed
    }
}
