using Microsoft.Data.Sqlite;
using Paperbunkr.Data.Gcd;
using Paperbunkr.Data.Tests.Gcd;

namespace Paperbunkr.Data.Tests.EventEnrichment;

public sealed class GcdEventEvidenceTests
{
    [Fact]
    public void SchemaOneStillWorksWithoutArcQueries()
    {
        using var dump = new FakeGcdDump().Series(1, "Original", 2009, "Marvel").Issue(10, 1, "1", "2009-08-00");
        dump.BuildExtract();
        using (var c = new SqliteConnection($"Data Source={dump.ExtractPath};Pooling=False"))
        { c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "UPDATE meta SET value='1' WHERE key='schema_version'"; cmd.ExecuteNonQuery(); }
        using var store = GcdDataStore.TryOpen(dump.ExtractPath);
        Assert.NotNull(store); Assert.False(store.HasEventData); Assert.Single(store.IssuesOf(1)); Assert.Empty(store.EventArcs([10, 11]));
    }

    [Fact]
    public void ExtractSeparatesReprintedStoryAndPreservesPartialDate()
    {
        using var dump = new FakeGcdDump().Series(1, "Original", 2009, "Marvel")
            .Series(2, "Collected", 2010, "Marvel").Issue(10, 1, "1", "2009-08-00").Issue(20, 2, "1", "2010-01-01");
        using (var c = new SqliteConnection($"Data Source={dump.DumpPath};Pooling=False"))
        {
            c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = """
                ALTER TABLE gcd_series ADD COLUMN publishing_format TEXT;
                ALTER TABLE gcd_issue ADD COLUMN on_sale_date_uncertain INTEGER DEFAULT 0;
                UPDATE gcd_series SET publishing_format='collected edition' WHERE id=2;
                CREATE TABLE gcd_story_arc(id INTEGER PRIMARY KEY,name TEXT,description TEXT,language_id INTEGER,year_first_published INTEGER,year_first_published_uncertain INTEGER,deleted INTEGER);
                CREATE TABLE gcd_story(id INTEGER PRIMARY KEY,issue_id INTEGER,deleted INTEGER);
                CREATE TABLE gcd_story_story_arc(id INTEGER PRIMARY KEY,story_id INTEGER,storyarc_id INTEGER);
                CREATE TABLE gcd_reprint(id INTEGER PRIMARY KEY,origin_id INTEGER,target_id INTEGER);
                INSERT INTO gcd_story_arc VALUES(1,'Arc','Event overview',1,2009,0,0);
                INSERT INTO gcd_story VALUES(100,10,0),(200,20,0);
                INSERT INTO gcd_story_story_arc VALUES(1,100,1),(2,200,1);
                INSERT INTO gcd_reprint VALUES(1,100,200);
                """; cmd.ExecuteNonQuery();
        }
        using var store = dump.Open();
        Assert.True(store.HasEventData);
        Assert.Single(store.EventArcs([10, 20]));
        var entries = store.EventIssues([], 1);
        Assert.Equal(2, entries.Count);
        Assert.False(entries.Single(i => i.Id == 10).Reprint);
        Assert.Equal("2009-08", entries.Single(i => i.Id == 10).Date!.Entry);
        Assert.True(entries.Single(i => i.Id == 20).Reprint);
        Assert.True(entries.Single(i => i.Id == 20).Collected);
    }
}
