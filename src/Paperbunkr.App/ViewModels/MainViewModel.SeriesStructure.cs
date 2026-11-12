using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Reader;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

public partial class MainViewModel
{
    private void WireSeriesStructure()
    {
        foreach (var band in new[] { Detail.Band, MangaDetail.Band })
        {
            band.OpenEntity = GoMetadataEntityByName;
            band.FillChipImages = EntityChipImages.FillAsync;
        }
        foreach (var tabs in new[] { Detail.Tabs, MangaDetail.Tabs })
        {
            tabs.OpenEntity = GoMetadataEntityByName;
            tabs.FillChipImages = EntityChipImages.FillAsync;
            tabs.OpenContinuity = GoContinuityForEntity;
            tabs.ReadQueue = GoReaderForQueue;
            tabs.OpenReadingList = GoReadingWithList;
        }
        MetadataEntityDetail.ReadQueue = GoReaderForQueue;
        MetadataEntityDetail.OpenContinuity = GoContinuityForEntity;
        MetadataEntityDetail.OpenEvent = GoStoryEvent;
        MetadataEntityDetail.OpenContinuityMap = (id, kind, entityId) => { GoEventsCommand.Execute(null); Events.OpenMapFeaturing(true, id, kind, entityId); };
        MetadataEntityDetail.OpenEventMap = (id, kind, entityId) => { GoEventsCommand.Execute(null); Events.OpenMapFeaturing(false, id, kind, entityId); };
        MetadataEntityDetail.OpenReadingList = GoReadingWithList;
        MetadataEntityDetail.ShowInLibrary = GoLibraryWithSearch;
        MetadataEntityDetail.Notify = ShowToast;
        MetadataEntityDetail.OpenEntity = (kind, id, continuityId) => GoMetadataEntityDetail(kind, id, continuityId);
        Reader.OpenEntityRequested += GoMetadataEntityByName;
        Events.OpenEntity = (kind, id, continuityId) => GoMetadataEntityDetail(kind, id, continuityId);
        Events.ShowInLibrary = GoLibraryWithSearch;
    }

    internal void GoMetadataEntityByName(ComicMetadataEntityKind kind, string name)
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            if (EntityNameLookup.Find(context, kind, name) is int id)
            {
                GoMetadataEntityDetail(kind, id);
                return;
            }
        }

        // Not indexed yet (the one-time rebuild may not have run): index just the issues that mention the name, off the UI thread, then open
        // the page. Only a name no issue mentions falls back to a Library search.
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                using var context = PaperbunkrDb.CreateContext();
                return EntityNameLookup.FindOrIndex(context, kind, name);
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException)
            {
                return null;
            }
        }).ContinueWith(t => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion && t.Result is int indexed) GoMetadataEntityDetail(kind, indexed);
            else GoLibraryWithSearch(name);
        }), System.Threading.Tasks.TaskScheduler.Default);
    }
    private void GoReaderForQueue(int issueId, ReadingQueue queue) => RunDrill(DrillTransitionKind.Push, $"issue-cover:{issueId}", () =>
    {
        NavigateToReaderCore(issueId, null, queue: queue);
        PushHistory(new NavigationEntry("reader", NavigationEntryKind.Issue, issueId, Reader.IssueTitle));
    });
    private void GoContinuityForEntity(int id)
    {
        GoEventsCommand.Execute(null);
        Events.LoadContinuity(id);
    }
}
