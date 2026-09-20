using Paperbunkr.Data.Organizing;
using Paperbunkr.Data.Tests.Acquisition;

namespace Paperbunkr.Data.Tests.Organizing;

public class OrganizerStoreTests : AcquisitionTestBase
{
    [Fact]
    public void Profiles_RoundTrip_IncludingTheMonthNameTable_AndListInNameOrder()
    {
        var store = new OrganizerProfileStore(NewContext);
        var b = store.Save(new OrganizerProfile { Name = "b profile", BaseFolder = "C:/lib", MonthNames = new Dictionary<int, string> { [1] = "Januar" }, Mode = OrganizerMode.Copy });
        store.Save(new OrganizerProfile { Name = "A profile" });

        Assert.NotEqual(0, b.Id);
        Assert.Equal(new[] { "A profile", "b profile" }, store.GetAll().Select(p => p.Name));
        var loaded = store.Get(b.Id)!;
        Assert.Equal("Januar", loaded.MonthNames[1]);
        Assert.Equal(OrganizerMode.Copy, loaded.Mode);

        loaded.Name = "renamed";
        store.Save(loaded);
        Assert.Equal("renamed", store.Get(b.Id)!.Name);

        store.Delete(b.Id);
        Assert.Null(store.Get(b.Id));
    }

    [Fact]
    public void AProfileWithNoMonthNames_UsesTheDefaults_AndACorruptTableDoesNotThrow()
    {
        Assert.Empty(new OrganizerProfile().MonthNames);
        Assert.Empty(new OrganizerProfile { MonthNamesJson = "{oops" }.MonthNames);
    }

    [Fact]
    public void Undo_MarksTheBatchRevertedInsteadOfDeletingIt_SoItIsNeverOfferedAgainButStaysAuditable()
    {
        var log = new OrganizeUndoLog(NewContext);
        int first = log.BeginBatch("p");
        log.Record(first, "old1", "new1");
        int second = log.BeginBatch("p");
        log.Record(second, "old2", "new2");
        log.Record(second, "old3", "new3");

        var last = log.GetLastBatch();
        Assert.Equal(new[] { "old3", "old2" }, last.Select(m => m.OldPath));        // newest move first

        log.MarkBatchReverted(second);

        Assert.Equal(new[] { "old1" }, log.GetLastBatch().Select(m => m.OldPath));   // the earlier batch is next
        Assert.Equal(3, Context.OrganizeMoves.Count());                              // nothing was deleted
        Assert.Equal(2, Context.OrganizeMoves.Count(m => m.IsReverted));

        log.MarkBatchReverted(first);
        Assert.Empty(log.GetLastBatch());
    }
}
