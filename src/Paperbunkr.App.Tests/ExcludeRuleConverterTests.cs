using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Plugins;
using Paperbunkr.App.Scraper.Import;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins.Automation;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="ExcludeRuleConverter"/> (docs/superpowers/specs/2026-10-04-lop-profile-import-design.md): a Library Organizer profile's exclude rules become the group the
/// organizer evaluates, exactly or not at all. The core check is an equivalence test: a port of the plugin's own <c>check_metadata_rules</c> decides which books it
/// would leave alone, and the converted rule, run through the app's real rules engine from its stored JSON, must leave exactly the same ones alone.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ExcludeRuleConverterTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pb_lop_rules_{Guid.NewGuid():N}.db");
    private readonly List<Issue> _books = new();

    public ExcludeRuleConverterTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.Migrate();

        // Every combination of the values the generated rules look at.
        foreach (bool complete in new[] { false, true })
        {
            var series = new Series { Name = complete ? "Done" : "Going", Status = complete ? SeriesStatus.Completed : SeriesStatus.Ongoing };
            context.Series.Add(series);
            foreach (string? format in new[] { "Annual", "TPB", null })
            foreach (string? publisher in new[] { "DC", "Marvel" })
            foreach (string? notes in new[] { "x", "y z" })
            foreach (int? count in new int?[] { null, 5, 30 })
            foreach (int? year in new int?[] { null, 1999, 2010 })
            {
                var issue = new Issue { Series = series, Number = "1", Format = format, Publisher = publisher, Notes = notes, Count = count, Year = year };
                context.Issues.Add(issue);
                _books.Add(issue);
            }
        }

        context.SaveChanges();
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

    // --- The plugin's own logic (locommon.py:203-362, 582-630), ported as the reference ---

    private static string Text(Issue b, string field) => field switch
    {
        "Format" => b.Format ?? string.Empty,
        "Publisher" => b.Publisher ?? string.Empty,
        _ => b.Notes ?? string.Empty,
    };

    private static int? RuleResult(LopRule r, Issue b)
    {
        switch (r.Field)
        {
            case "Count" or "Year":
                int data = r.Field == "Count" ? b.Count ?? -1 : b.Year ?? -1;
                int value = int.Parse(r.Value);
                return r.Operator switch { "greater than" => value < data ? 1 : 0, "less than" => value > data ? 1 : 0, _ => null };
            case "Series Complete":
                bool complete = b.Series!.IsComplete;
                return r.Operator switch { "is" => complete ? 1 : 0, "is not" => !complete ? 1 : 0, _ => null };
            default:
                string text = Text(b, r.Field);
                return r.Operator switch
                {
                    "is" => text == r.Value ? 1 : 0,
                    "is not" => text != r.Value ? 1 : 0,
                    "contains" => text.Contains(r.Value, StringComparison.Ordinal) ? 1 : 0,
                    "does not contain" => !text.Contains(r.Value, StringComparison.Ordinal) ? 1 : 0,
                    _ => null,
                };
        }
    }

    private static int? GroupResult(string op, IReadOnlyList<LopRuleNode> children, Issue b)
    {
        int count = 0, total = 0;
        foreach (LopRuleNode child in children)
        {
            int? result = child is LopRule r ? RuleResult(r, b) : GroupResult(((LopRuleGroup)child).Operator, ((LopRuleGroup)child).Children, b);
            if (result is null)
            {
                continue;
            }

            count += result.Value;
            total++;
        }

        return total == 0 ? null : op == "Any" ? (count > 0 ? 1 : 0) : (count == total ? 1 : 0);
    }

    /// <summary>The plugin's <c>check_metadata_rules</c>: true when the book is organized.</summary>
    private static bool PluginOrganizes(LopExcludeRules rules, Issue b)
    {
        int count = 0, total = 0;
        foreach (LopRuleNode node in rules.Rules)
        {
            int? result = node is LopRule r ? RuleResult(r, b) : GroupResult(((LopRuleGroup)node).Operator, ((LopRuleGroup)node).Children, b);
            if (result is null)
            {
                continue;
            }

            count += result.Value;
            total++;
        }

        if (total == 0)
        {
            return true;
        }

        bool qualifies = rules.Operator == "Any" ? count > 0 : count == total;
        return rules.ExcludeMode == "Only" ? qualifies : !qualifies;
    }

    /// <summary>The ids our organizer would leave alone: the stored JSON, read back and run through the real rules engine, as <c>OrganizeCoordinator</c> does.</summary>
    private static HashSet<int> Excluded(ExcludeConversion conversion)
    {
        if (conversion.Group is null)
        {
            return new HashSet<int>();
        }

        string json = JsonSerializer.Serialize(conversion.Group);
        var rule = JsonSerializer.Deserialize<PluginConditionGroup>(json)!;
        return new PaperbunkrRulesEngine().Evaluate(rule).Select(i => i.Id).ToHashSet();
    }

    private HashSet<int> PluginSkips(LopExcludeRules rules) => _books.Where(b => !PluginOrganizes(rules, b)).Select(b => b.Id).ToHashSet();

    private static LopRule R(string field, string op, string value) => new(field, op, value);

    private static LopRuleGroup G(string op, params LopRuleNode[] children) => new(op, children);

    private static LopExcludeRules Rules(string op, string mode, params LopRuleNode[] rules) => new(op, mode, rules);

    // --- The equivalence check ---

    [Fact]
    public void TheSuppliedMarvelDcProfile_LeavesAloneExactlyTheBooksThePluginWould()
    {
        // 01 - Marvel-DC - Main/Limited/B&W/Annual.lop: All of [ Any(Format is ...), All(Tags does not contain ...), Any(Publisher is ...) ], Only.
        var rules = Rules("All", "Only",
            G("Any", R("Format", "is", "Annual"), R("Format", "is", "TPB")),
            G("All", R("Notes", "does not contain", "z"), R("Notes", "does not contain", "q")),
            G("Any", R("Publisher", "is", "Marvel"), R("Publisher", "is", "DC")));

        var conversion = ExcludeRuleConverter.Convert(rules);

        Assert.False(conversion.Rejected);
        Assert.Equal(PluginSkips(rules), Excluded(conversion));
        Assert.NotEmpty(Excluded(conversion));
        Assert.NotEqual(_books.Count, Excluded(conversion).Count);
    }

    [Fact]
    public void RandomRuleTrees_InEitherMode_LeaveAloneExactlyTheBooksThePluginWould()
    {
        var random = new Random(20261010);
        for (int round = 0; round < 250; round++)
        {
            string mode = random.Next(2) == 0 ? "Only" : "Do not";
            var rules = new LopExcludeRules(random.Next(2) == 0 ? "Any" : "All", mode, RandomNodes(random, depth: 0));

            var conversion = ExcludeRuleConverter.Convert(rules);

            Assert.False(conversion.Rejected, conversion.RejectedBecause);
            Assert.True(PluginSkips(rules).SetEquals(Excluded(conversion)), $"round {round}: {mode}, {rules.Operator}, {Describe(rules.Rules)}");
        }
    }

    private static List<LopRuleNode> RandomNodes(Random random, int depth)
    {
        var nodes = new List<LopRuleNode>();
        int n = random.Next(0, 4);
        for (int i = 0; i < n; i++)
        {
            if (depth < 2 && random.Next(4) == 0)
            {
                nodes.Add(new LopRuleGroup(random.Next(2) == 0 ? "Any" : "All", RandomNodes(random, depth + 1)));
            }
            else
            {
                nodes.Add(RandomRule(random));
            }
        }

        return nodes;
    }

    private static LopRule RandomRule(Random random)
    {
        switch (random.Next(3))
        {
            case 0:
                string field = new[] { "Format", "Publisher", "Notes" }[random.Next(3)];
                string op = new[] { "is", "is not", "contains", "does not contain" }[random.Next(4)];
                string value = new[] { "Annual", "TPB", "DC", "Marvel", "x", "y", "z", string.Empty }[random.Next(8)];
                return new LopRule(field, op, value);
            case 1:
                return random.Next(2) == 0
                    ? new LopRule("Count", random.Next(2) == 0 ? "greater than" : "less than", new[] { "0", "5", "10", "30" }[random.Next(4)])
                    : new LopRule("Year", random.Next(2) == 0 ? "greater than" : "less than", new[] { "1999", "2000", "2010" }[random.Next(3)]);
            default:
                return new LopRule("Series Complete", random.Next(2) == 0 ? "is" : "is not", "Yes");
        }
    }

    private static string Describe(IReadOnlyList<LopRuleNode> nodes) =>
        string.Join("; ", nodes.Select(n => n is LopRule r ? $"{r.Field} {r.Operator} '{r.Value}'" : $"[{((LopRuleGroup)n).Operator}: {Describe(((LopRuleGroup)n).Children)}]"));

    // --- Shape ---

    [Fact]
    public void DoNot_KeepsTheRulesAsTheyAre_CaseSensitive()
    {
        var conversion = ExcludeRuleConverter.Convert(Rules("Any", "Do not", R("Format", "is", "TPB"), R("Publisher", "does not contain", "DC")));

        var group = conversion.Group!;
        Assert.Equal(SmartListGroupMode.Or, group.Mode);
        Assert.Equal(2, group.Conditions.Count);
        Assert.All(group.Conditions, c => Assert.False(c.IgnoreCase));
        Assert.Contains(group.Conditions, c => c.Field == SmartListField.Format && c.Op == SmartListOperator.Is && c.Value == "TPB" && !c.Not);
        Assert.Contains(group.Conditions, c => c.Field == SmartListField.Publisher && c.Op == SmartListOperator.Contains && c.Value == "DC" && c.Not);
    }

    [Fact]
    public void Only_InvertsByDeMorgan_ModesAndNotsFlip()
    {
        var conversion = ExcludeRuleConverter.Convert(Rules("All", "Only", R("Format", "is", "TPB"), G("Any", R("Publisher", "is not", "DC"))));

        var group = conversion.Group!;
        Assert.Equal(SmartListGroupMode.Or, group.Mode);
        Assert.True(Assert.Single(group.Conditions).Not);
        var child = Assert.Single(group.ChildGroups);
        Assert.Equal(SmartListGroupMode.And, child.Mode);
        Assert.False(Assert.Single(child.Conditions).Not);
    }

    [Theory]
    [InlineData("Only")]
    [InlineData("Do not")]
    public void NoRules_MoveEverything_InEitherMode(string mode)
    {
        var conversion = ExcludeRuleConverter.Convert(Rules("All", mode));

        Assert.False(conversion.Rejected);
        Assert.Null(conversion.Group);
    }

    [Fact]
    public void AGroupWithNoRules_IsIgnored_LikeThePlugin()
    {
        var rules = Rules("All", "Do not", R("Format", "is", "TPB"), G("Any"), G("All", G("Any")));

        var conversion = ExcludeRuleConverter.Convert(rules);

        Assert.Empty(conversion.Group!.ChildGroups);
        Assert.Single(conversion.Group.Conditions);
        Assert.Equal(PluginSkips(rules), Excluded(conversion));
    }

    [Fact]
    public void OnlyEmptyGroups_IsTheSameAsNoRules()
    {
        Assert.Null(ExcludeRuleConverter.Convert(Rules("Any", "Only", G("All"))).Group);
    }

    [Fact]
    public void ANumericRule_BecomesAWholeNumberComparison()
    {
        var group = ExcludeRuleConverter.Convert(Rules("All", "Do not", R("Count", "greater than", "12"), R("Year", "less than", "2000"))).Group!;

        Assert.Contains(group.Conditions, c => c.Field == SmartListField.Count && c.Op == SmartListOperator.GreaterThan && c.Value == "12");
        Assert.Contains(group.Conditions, c => c.Field == SmartListField.Year && c.Op == SmartListOperator.LessThan && c.Value == "2000");
    }

    [Fact]
    public void AYesNoRule_IsAToggle_AndIsNotYesIsTheNegation()
    {
        var group = ExcludeRuleConverter.Convert(Rules("Any", "Do not", R("Series Complete", "is", "Yes"), R("Black And White", "is not", "Yes"))).Group!;

        var complete = group.Conditions.Single(c => c.Field == SmartListField.SeriesComplete);
        Assert.Equal("true", complete.Value);
        Assert.False(complete.Not);
        var bw = group.Conditions.Single(c => c.Field == SmartListField.BlackAndWhite);
        Assert.Equal("true", bw.Value);
        Assert.True(bw.Not);
    }

    [Fact]
    public void FieldNamesAreMatchedIgnoringCase()
    {
        Assert.False(ExcludeRuleConverter.Convert(Rules("Any", "Do not", R("main character or team", "is", "x"))).Rejected);
    }

    // --- Rules that cannot be carried over exactly reject the whole profile ---

    [Theory]
    [InlineData("Start Year", "is", "2000")]
    [InlineData("End Month", "is", "1")]
    [InlineData("Added Date", "is", "2000")]
    [InlineData("Manga", "is", "Yes")]
    [InlineData("File Format", "is", "CBZ")]
    [InlineData("Counter", "is", "1")]
    [InlineData("Nonsense", "is", "x")]
    public void AFieldWithNoExactEquivalent_RejectsTheProfile(string field, string op, string value)
    {
        var conversion = ExcludeRuleConverter.Convert(Rules("All", "Do not", R("Format", "is", "TPB"), R(field, op, value)));

        Assert.True(conversion.Rejected);
        Assert.Null(conversion.Group);
        Assert.Contains(field, conversion.RejectedBecause);
    }

    [Theory]
    [InlineData("Count", "is", "5")]
    [InlineData("Year", "contains", "1")]
    [InlineData("Count", "does not contain", "5")]
    [InlineData("Count", "is not", "5")]
    public void ANumericRuleThatNeverMatchesInThePlugin_RejectsTheProfile(string field, string op, string value)
    {
        Assert.True(ExcludeRuleConverter.Convert(Rules("All", "Do not", R(field, op, value))).Rejected);
    }

    [Fact]
    public void AGreaterThanOnText_RejectsTheProfile()
    {
        Assert.True(ExcludeRuleConverter.Convert(Rules("All", "Do not", R("Publisher", "greater than", "A"))).Rejected);
    }

    [Fact]
    public void ANumericRuleWithoutAWholeNumber_RejectsTheProfile()
    {
        Assert.True(ExcludeRuleConverter.Convert(Rules("All", "Do not", R("Count", "greater than", "many"))).Rejected);
    }

    [Theory]
    [InlineData("No")]
    [InlineData("Unknown")]
    public void AYesNoRuleForAnythingButYes_RejectsTheProfile(string value)
    {
        Assert.True(ExcludeRuleConverter.Convert(Rules("All", "Do not", R("Series Complete", "is", value))).Rejected);
    }

    [Fact]
    public void AnUnknownOperator_RejectsTheProfile()
    {
        Assert.True(ExcludeRuleConverter.Convert(Rules("All", "Do not", R("Format", "resembles", "x"))).Rejected);
    }

    [Fact]
    public void AnUnknownRootOperatorOrMode_RejectsTheProfile()
    {
        Assert.True(ExcludeRuleConverter.Convert(Rules("Most", "Do not", R("Format", "is", "x"))).Rejected);
        Assert.True(ExcludeRuleConverter.Convert(Rules("Any", "Sometimes", R("Format", "is", "x"))).Rejected);
        Assert.True(ExcludeRuleConverter.Convert(Rules("Any", "Do not", G("Most", R("Format", "is", "x")))).Rejected);
    }

    [Fact]
    public void ARejectedRuleDeepInsideAGroup_StillRejectsTheWholeProfile()
    {
        var rules = Rules("All", "Only", G("Any", R("Format", "is", "x"), G("All", R("Start Year", "is", "2000"))));

        Assert.True(ExcludeRuleConverter.Convert(rules).Rejected);
    }
}
