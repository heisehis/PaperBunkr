using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="CollectionResolver"/> (docs/superpowers/specs/2026-08-27-collections-
/// design.md) - previously untested. Adds coverage for the hybrid manual+rule-matched union
/// (docs/superpowers/specs/2026-08-30-smart-collections-design.md): dedup when a manually-added
/// item also matches its collection's rule, and that <see cref="CollectionResolver.GetOtherSeriesSharingCollection"/>/
/// <see cref="CollectionResolver.GetCoverHint"/> pick up rule-matched members too.
/// </summary>
public class CollectionResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly PaperbunkrDbContext _context;

    public CollectionResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_collection_resolver_test_{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _context = new PaperbunkrDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private Series AddSeries(string name)
    {
        var series = new Series { Name = name };
        _context.Series.Add(series);
        _context.SaveChanges();
        return series;
    }

    private Collection AddCollection(string name = "C")
    {
        var collection = new Collection { Name = name };
        _context.Collections.Add(collection);
        _context.SaveChanges();
        return collection;
    }

    private void AddManualSeries(Collection collection, Series series, int sortOrder = 0)
    {
        _context.CollectionItems.Add(new CollectionItem { CollectionId = collection.Id, SeriesId = series.Id, SortOrder = sortOrder });
        _context.SaveChanges();
    }

    private int AddSeriesRule(string matchName)
    {
        var list = new SmartList
        {
            Name = "series rule",
            TargetKind = SmartListTargetKind.Series,
            RootGroup = new SmartListConditionGroup
            {
                Mode = SmartListGroupMode.And,
                Conditions = [new SmartListCondition { Field = SmartListField.SeriesName, Operator = SmartListOperator.Is, Value = matchName }],
            },
        };
        _context.SmartLists.Add(list);
        _context.SaveChanges();
        return list.Id;
    }

    [Fact]
    public void GetMembers_ManualOnly_UnchangedFromBeforeSmartCollections()
    {
        var collection = AddCollection();
        var series = AddSeries("Alpha");
        AddManualSeries(collection, series);

        var members = CollectionResolver.GetMembers(_context, collection.Id);

        var member = Assert.Single(members);
        Assert.Equal(series.Id, member.TargetId);
        Assert.NotNull(member.CollectionItemId);
    }

    [Fact]
    public void GetMembers_UnionsRuleMatchedSeriesAlongsideManualOnes()
    {
        var collection = AddCollection();
        var manual = AddSeries("Manual Pick");
        var ruleMatched = AddSeries("Rule Match");
        AddManualSeries(collection, manual);

        int ruleId = AddSeriesRule("Rule Match");
        collection.SeriesSmartListId = ruleId;
        _context.SaveChanges();

        var members = CollectionResolver.GetMembers(_context, collection.Id);

        Assert.Equal(2, members.Count);
        var manualMember = Assert.Single(members, m => m.TargetId == manual.Id);
        Assert.NotNull(manualMember.CollectionItemId);
        var ruleMember = Assert.Single(members, m => m.TargetId == ruleMatched.Id);
        Assert.Null(ruleMember.CollectionItemId);
    }

    [Fact]
    public void GetMembers_DedupsWhenManualItemAlsoMatchesTheRule()
    {
        var collection = AddCollection();
        var series = AddSeries("Both");
        AddManualSeries(collection, series);

        int ruleId = AddSeriesRule("Both");
        collection.SeriesSmartListId = ruleId;
        _context.SaveChanges();

        var members = CollectionResolver.GetMembers(_context, collection.Id);

        var member = Assert.Single(members);
        Assert.NotNull(member.CollectionItemId); // the manual row wins, not a synthetic duplicate
    }

    [Fact]
    public void GetMembers_RuleMatchedRows_HaveNoCollectionItemId()
    {
        var collection = AddCollection();
        AddSeries("Matched"); // no manual membership at all
        int ruleId = AddSeriesRule("Matched");
        collection.SeriesSmartListId = ruleId;
        _context.SaveChanges();

        var member = Assert.Single(CollectionResolver.GetMembers(_context, collection.Id));
        Assert.Null(member.CollectionItemId);
        Assert.Equal(CollectionMemberKind.Series, member.Kind);
    }

    [Fact]
    public void GetMembers_NoRuleSlots_BehavesExactlyAsManualCollection()
    {
        var collection = AddCollection();
        Assert.Empty(CollectionResolver.GetMembers(_context, collection.Id));
    }

    [Fact]
    public void GetOtherSeriesSharingCollection_IncludesRuleMatchedMembership()
    {
        var collection = AddCollection();
        var anchor = AddSeries("Anchor");
        var ruleMatched = AddSeries("Also Here");
        AddManualSeries(collection, anchor);

        int ruleId = AddSeriesRule("Also Here");
        collection.SeriesSmartListId = ruleId;
        _context.SaveChanges();

        var others = CollectionResolver.GetOtherSeriesSharingCollection(_context, anchor.Id);

        Assert.Equal([ruleMatched.Id], others.Select(s => s.Id));
    }

    [Fact]
    public void GetCoverHint_FirstMember_CanBeARuleMatchedSeries()
    {
        var collection = AddCollection();
        var series = AddSeries("Only Member");
        int ruleId = AddSeriesRule("Only Member");
        collection.SeriesSmartListId = ruleId;
        _context.SaveChanges();

        var hint = CollectionResolver.GetCoverHint(_context, collection.Id);

        Assert.NotNull(hint.FirstMember);
        Assert.Equal(series.Id, hint.FirstMember!.TargetId);
    }
}
