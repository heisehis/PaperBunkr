using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Plugin smart fields in the Smart List engine (Plugin API 4.5, docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-
/// design.md §D1): a <see cref="SmartListField.CustomValue"/> condition on a registered plugin field compares as its declared type, a
/// missing value of a typed field never matches, and an unregistered name keeps CE's text comparison. The registry is process-wide,
/// so these tests use field names no other test touches and restore it afterwards.
/// </summary>
public sealed class PluginSmartFieldQueryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_pluginfields_{Guid.NewGuid():N}.db");
    private readonly PaperbunkrDbContext _context;
    private readonly IReadOnlyList<PluginSmartField> _saved = PluginFieldTypeRegistry.All;

    public PluginSmartFieldQueryTests()
    {
        _context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        _context.Database.EnsureCreated();
        var series = new Series { Name = "Kilo Station", SortName = "Kilo Station" };
        _context.Series.Add(series);
        Issue Make(string number, params (string Name, string Value)[] values)
        {
            var issue = new Issue { Series = series, Number = number };
            foreach (var (name, value) in values)
            {
                issue.CustomValues.Add(new IssueCustomValue { Name = name, Value = value });
            }

            _context.Issues.Add(issue);
            return issue;
        }

        Make("1", ("pfq.days", "3"), ("pfq.synced", "2026-01-10"), ("pfq.flagged", "true"), ("pfq.mood", "Calm"));
        Make("2", ("pfq.days", "40"), ("pfq.synced", "2026-06-01"), ("pfq.flagged", "false"));
        Make("3", ("pfq.days", "not a number"));
        Make("4");
        _context.SaveChanges();

        PluginFieldTypeRegistry.Replace(new[]
        {
            new PluginSmartField("pfq.days", "Days since read (Test)", SmartListDataType.Number),
            new PluginSmartField("pfq.synced", "Last synced (Test)", SmartListDataType.Date),
            new PluginSmartField("pfq.flagged", "Flagged (Test)", SmartListDataType.Toggle),
            new PluginSmartField("pfq.mood", "Mood (Test)", SmartListDataType.Text),
        });
    }

    public void Dispose()
    {
        PluginFieldTypeRegistry.Replace(_saved);
        _context.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private List<string> Numbers(string name, SmartListOperator op, string value, string? value2 = null) =>
        SmartListQueryBuilder.Build(_context, new SmartList
        {
            Name = "t",
            RootGroup = new SmartListConditionGroup
            {
                Mode = SmartListGroupMode.And,
                Conditions = new List<SmartListCondition> { new() { Field = SmartListField.CustomValue, CustomValueName = name, Operator = op, Value = value, Value2 = value2 } },
            },
        }).Select(i => i.Number!).OrderBy(n => n).ToList();

    [Fact]
    public void A_number_field_compares_numerically_not_as_text()
    {
        // As text "40" < "5"; as numbers it isn't.
        Assert.Equal(new[] { "2" }, Numbers("pfq.days", SmartListOperator.GreaterThan, "5"));
        Assert.Equal(new[] { "1" }, Numbers("pfq.days", SmartListOperator.LessThan, "5"));
        Assert.Equal(new[] { "1", "2" }, Numbers("pfq.days", SmartListOperator.InRange, "1", "100"));
    }

    [Fact]
    public void A_missing_or_unreadable_typed_value_never_matches()
    {
        Assert.DoesNotContain("3", Numbers("pfq.days", SmartListOperator.IsNot, "999"));
        Assert.DoesNotContain("4", Numbers("pfq.days", SmartListOperator.IsNot, "999"));
    }

    [Fact]
    public void A_date_field_compares_as_dates()
    {
        Assert.Equal(new[] { "2" }, Numbers("pfq.synced", SmartListOperator.IsAfter, "2026-03-01"));
        Assert.Equal(new[] { "1" }, Numbers("pfq.synced", SmartListOperator.IsBefore, "2026-03-01"));
    }

    [Fact]
    public void A_toggle_field_compares_as_yes_or_no()
    {
        Assert.Equal(new[] { "1" }, Numbers("pfq.flagged", SmartListOperator.Is, "true"));
        Assert.Equal(new[] { "2" }, Numbers("pfq.flagged", SmartListOperator.Is, "false"));
    }

    [Fact]
    public void A_text_field_and_an_unregistered_name_keep_the_text_comparison()
    {
        // CE: a missing custom value is the empty string, so "is ''" matches every issue without it.
        Assert.Equal(new[] { "2", "3", "4" }, Numbers("pfq.mood", SmartListOperator.Is, ""));
        Assert.Equal(new[] { "1" }, Numbers("pfq.mood", SmartListOperator.Is, "calm"));

        PluginFieldTypeRegistry.Replace(Array.Empty<PluginSmartField>());
        Assert.Equal(new[] { "1", "3" }, Numbers("pfq.days", SmartListOperator.Contains, "3").Concat(Numbers("pfq.days", SmartListOperator.Contains, "number")).OrderBy(n => n).ToList());
    }
}
