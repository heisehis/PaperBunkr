using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// SmartList Engine v2 (docs/superpowers/specs/2026-08-28-smartlist-engine-v2-design.md §5):
/// recursive AND/OR group evaluation with per-condition NOT (§2), the two new text operators and
/// the case-sensitivity toggle (§3), and <see cref="SmartListField.AllProperties"/> (§4).
/// </summary>
public class SmartListEngineV2Tests : IDisposable
{
    private readonly string _dbPath;
    private readonly PaperbunkrDbContext _context;

    // Genre / Publisher / Writer (a comma-delimited list field):
    private readonly int _horrorAcmeId;   // Horror  / Acme   / "Alan Moore, Grant Morrison"
    private readonly int _sciFiZenithId;  // Sci-Fi  / Zenith / "Leeroy Jenkins, grant morrison"
    private readonly int _horrorZenithId; // Horror  / Zenith / "Junji Ito, Lee"

    public SmartListEngineV2Tests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_smartv2_{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _context = new PaperbunkrDbContext(options);
        _context.Database.EnsureCreated();

        var series = new Series { Name = "Anthology" };
        _context.Series.Add(series);
        _context.SaveChanges();

        Issue Make(string genre, string publisher, string writer)
        {
            var issue = new Issue { SeriesId = series.Id, Number = "1", Publisher = publisher, Writer = writer };
            issue.MergeFrom(IssueTagField.Genre, new[] { genre });
            return issue;
        }

        var a = Make("Horror", "Acme", "Alan Moore, Grant Morrison");
        var b = Make("Sci-Fi", "Zenith", "Leeroy Jenkins, grant morrison");
        var c = Make("Horror", "Zenith", "Junji Ito, Lee");
        _context.Issues.AddRange(a, b, c);
        _context.SaveChanges();
        _horrorAcmeId = a.Id;
        _sciFiZenithId = b.Id;
        _horrorZenithId = c.Id;
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

    private static SmartListCondition Cond(SmartListField field, SmartListOperator op, string value, bool not = false, bool ignoreCase = true) =>
        new() { Field = field, Operator = op, Value = value, Not = not, IgnoreCase = ignoreCase };

    private List<int> Match(SmartListConditionGroup root) =>
        SmartListQueryBuilder.Build(_context, new SmartList { Name = "t", RootGroup = root }).Select(i => i.Id).OrderBy(x => x).ToList();

    private static int[] Sorted(params int[] ids) => ids.OrderBy(x => x).ToArray();

    // --- §2 nested groups + NOT ---

    [Fact]
    public void RootAndGroup_WithNoConditions_MatchesEveryIssue()
    {
        Assert.Equal(3, Match(new SmartListConditionGroup { Mode = SmartListGroupMode.And }).Count);
    }

    [Fact]
    public void OrGroup_MatchesUnionOfItsConditions()
    {
        var root = new SmartListConditionGroup
        {
            Mode = SmartListGroupMode.Or,
            Conditions =
            {
                Cond(SmartListField.Publisher, SmartListOperator.Is, "Acme"),
                Cond(SmartListField.Genre, SmartListOperator.Is, "Sci-Fi"),
            },
        };

        Assert.Equal(Sorted(_horrorAcmeId, _sciFiZenithId), Match(root));
    }

    [Fact]
    public void PerConditionNot_NegatesJustThatConditionInsideAnAndGroup()
    {
        // Genre is Horror AND NOT (Publisher is Acme)  ->  only the Horror/Zenith issue.
        var root = new SmartListConditionGroup
        {
            Mode = SmartListGroupMode.And,
            Conditions =
            {
                Cond(SmartListField.Genre, SmartListOperator.Is, "Horror"),
                Cond(SmartListField.Publisher, SmartListOperator.Is, "Acme", not: true),
            },
        };

        Assert.Equal(new[] { _horrorZenithId }, Match(root));
    }

    [Fact]
    public void NestedGroups_AndOfOr_EvaluateRecursively()
    {
        // (Publisher is Zenith) AND (Genre is Horror OR Genre is Sci-Fi)  ->  both Zenith issues.
        var root = new SmartListConditionGroup
        {
            Mode = SmartListGroupMode.And,
            Conditions = { Cond(SmartListField.Publisher, SmartListOperator.Is, "Zenith") },
            ChildGroups =
            {
                new SmartListConditionGroup
                {
                    Mode = SmartListGroupMode.Or,
                    Conditions =
                    {
                        Cond(SmartListField.Genre, SmartListOperator.Is, "Horror"),
                        Cond(SmartListField.Genre, SmartListOperator.Is, "Sci-Fi"),
                    },
                },
            },
        };

        Assert.Equal(Sorted(_sciFiZenithId, _horrorZenithId), Match(root));
    }

    [Fact]
    public void DeeplyNestedGroups_CombineByEachGroupsOwnMode()
    {
        // Horror AND ( Publisher Acme OR ( Publisher Zenith AND NOT Genre Sci-Fi ) )
        //   a: Horror, Acme                 -> inner-left true  -> match
        //   b: Sci-Fi -> fails top-level Horror
        //   c: Horror, Zenith, not Sci-Fi   -> inner-right true -> match
        var root = new SmartListConditionGroup
        {
            Mode = SmartListGroupMode.And,
            Conditions = { Cond(SmartListField.Genre, SmartListOperator.Is, "Horror") },
            ChildGroups =
            {
                new SmartListConditionGroup
                {
                    Mode = SmartListGroupMode.Or,
                    Conditions = { Cond(SmartListField.Publisher, SmartListOperator.Is, "Acme") },
                    ChildGroups =
                    {
                        new SmartListConditionGroup
                        {
                            Mode = SmartListGroupMode.And,
                            Conditions =
                            {
                                Cond(SmartListField.Publisher, SmartListOperator.Is, "Zenith"),
                                Cond(SmartListField.Genre, SmartListOperator.Is, "Sci-Fi", not: true),
                            },
                        },
                    },
                },
            },
        };

        Assert.Equal(Sorted(_horrorAcmeId, _horrorZenithId), Match(root));
    }

    [Fact]
    public void FlatAndGroup_ProducesIdenticalResultsToV1MultiConditionAnd()
    {
        // Regression guard for the migration's zero-data-loss claim: a single And root group with a
        // flat condition list is exactly v1 semantics.
        var root = new SmartListConditionGroup
        {
            Mode = SmartListGroupMode.And,
            Conditions =
            {
                Cond(SmartListField.Genre, SmartListOperator.Is, "Horror"),
                Cond(SmartListField.Publisher, SmartListOperator.Is, "Zenith"),
            },
        };

        Assert.Equal(new[] { _horrorZenithId }, Match(root));
    }

    // --- §3 ListContains vs Contains ---

    [Fact]
    public void ListContains_MatchesAWholeDelimitedItem_NotASubstringOfOne()
    {
        // "Lee" is a whole comma-item of the Horror/Zenith issue ("Junji Ito, Lee").
        var listContains = new SmartListConditionGroup { Conditions = { Cond(SmartListField.Writer, SmartListOperator.ListContains, "Lee") } };
        Assert.Equal(new[] { _horrorZenithId }, Match(listContains));

        // Substring Contains also catches "Lee" inside "Leeroy Jenkins".
        var contains = new SmartListConditionGroup { Conditions = { Cond(SmartListField.Writer, SmartListOperator.Contains, "Lee") } };
        Assert.Equal(Sorted(_sciFiZenithId, _horrorZenithId), Match(contains));
    }

    [Fact]
    public void ListContains_DoesNotMatchASubstringOfAnItem()
    {
        var root = new SmartListConditionGroup { Conditions = { Cond(SmartListField.Writer, SmartListOperator.ListContains, "Moore") } };
        Assert.Empty(Match(root)); // the item is "Alan Moore"; "Moore" alone is not
    }

    [Fact]
    public void ListContains_RespectsCaseSensitivityToggle()
    {
        var insensitive = new SmartListConditionGroup { Conditions = { Cond(SmartListField.Writer, SmartListOperator.ListContains, "grant morrison", ignoreCase: true) } };
        Assert.Equal(Sorted(_horrorAcmeId, _sciFiZenithId), Match(insensitive));

        var sensitive = new SmartListConditionGroup { Conditions = { Cond(SmartListField.Writer, SmartListOperator.ListContains, "grant morrison", ignoreCase: false) } };
        Assert.Equal(new[] { _sciFiZenithId }, Match(sensitive)); // only the literally-lowercase one
    }

    // --- §3 RegularExpression ---

    [Fact]
    public void RegularExpression_HappyPath_Matches()
    {
        var root = new SmartListConditionGroup { Conditions = { Cond(SmartListField.Publisher, SmartListOperator.RegularExpression, "^Z.*h$") } };
        Assert.Equal(Sorted(_sciFiZenithId, _horrorZenithId), Match(root));
    }

    [Fact]
    public void RegularExpression_MalformedPattern_SilentlyMatchesNothing_NeverThrows()
    {
        var root = new SmartListConditionGroup { Conditions = { Cond(SmartListField.Publisher, SmartListOperator.RegularExpression, "((((unbalanced") } };
        Assert.Empty(Match(root));
    }

    [Fact]
    public void RegularExpression_RespectsCaseSensitivityToggle()
    {
        var insensitive = new SmartListConditionGroup { Conditions = { Cond(SmartListField.Publisher, SmartListOperator.RegularExpression, "zenith", ignoreCase: true) } };
        Assert.Equal(2, Match(insensitive).Count);

        var sensitive = new SmartListConditionGroup { Conditions = { Cond(SmartListField.Publisher, SmartListOperator.RegularExpression, "zenith", ignoreCase: false) } };
        Assert.Empty(Match(sensitive));
    }

    // --- §3 IgnoreCase on the plain operators ---

    [Fact]
    public void Is_IsCaseSensitive_WhenIgnoreCaseFalse()
    {
        Assert.Empty(Match(new SmartListConditionGroup { Conditions = { Cond(SmartListField.Publisher, SmartListOperator.Is, "acme", ignoreCase: false) } }));
        Assert.Equal(new[] { _horrorAcmeId }, Match(new SmartListConditionGroup { Conditions = { Cond(SmartListField.Publisher, SmartListOperator.Is, "acme", ignoreCase: true) } }));
    }

    // --- §4 AllProperties ---

    [Fact]
    public void AllProperties_DefaultMode_MatchesAcrossTheFullIssueBundle()
    {
        var root = new SmartListConditionGroup
        {
            Conditions = { new SmartListCondition { Field = SmartListField.AllProperties, Operator = SmartListOperator.Contains, Value = "Junji", SearchMode = null } },
        };
        Assert.Equal(new[] { _horrorZenithId }, Match(root));
    }

    [Fact]
    public void AllProperties_ScopedToWriterMode_OnlySearchesThatBundle()
    {
        var writerScoped = new SmartListConditionGroup
        {
            Conditions = { new SmartListCondition { Field = SmartListField.AllProperties, Operator = SmartListOperator.Contains, Value = "Acme", SearchMode = SearchMode.Writer } },
        };
        Assert.Empty(Match(writerScoped)); // Publisher is not in the Writer bundle

        var allScoped = new SmartListConditionGroup
        {
            Conditions = { new SmartListCondition { Field = SmartListField.AllProperties, Operator = SmartListOperator.Contains, Value = "Acme", SearchMode = SearchMode.All } },
        };
        Assert.Equal(new[] { _horrorAcmeId }, Match(allScoped));
    }

    [Fact]
    public void AllProperties_GetsTheNewOperatorsForFree()
    {
        var listContains = new SmartListConditionGroup
        {
            Conditions = { new SmartListCondition { Field = SmartListField.AllProperties, Operator = SmartListOperator.ListContains, Value = "Junji Ito", SearchMode = SearchMode.All } },
        };
        Assert.Equal(new[] { _horrorZenithId }, Match(listContains));
    }
}
