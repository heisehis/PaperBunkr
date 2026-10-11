using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Guards the gallery's template list (docs/superpowers/specs/2026-10-06-smart-features-design.md §3.2): every rule must use a field
/// its kind's catalog has and an operator that field's data type supports, so a template can never create a list the editor
/// cannot show or the engine cannot evaluate.
/// </summary>
public class SmartListTemplateCatalogTests : IDisposable
{
    private readonly string _dbPath;
    private readonly PaperbunkrDbContext _context;

    public SmartListTemplateCatalogTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_smart_templates_test_{Guid.NewGuid():N}.db");
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

    // Mirrors Paperbunkr.App's SmartListOptionLabels.For - the operators the rule editor offers per data type.
    private static SmartListOperator[] OperatorsFor(SmartListDataType type) => type switch
    {
        SmartListDataType.Number => [SmartListOperator.Is, SmartListOperator.IsNot, SmartListOperator.GreaterThan, SmartListOperator.LessThan, SmartListOperator.InRange],
        SmartListDataType.Toggle => [SmartListOperator.Is, SmartListOperator.IsNot],
        SmartListDataType.Date => [SmartListOperator.Is, SmartListOperator.IsAfter, SmartListOperator.IsBefore, SmartListOperator.WithinLastDays, SmartListOperator.DateInRange],
        _ =>
        [
            SmartListOperator.Is, SmartListOperator.IsNot, SmartListOperator.Contains, SmartListOperator.ContainsAny, SmartListOperator.ContainsAll,
            SmartListOperator.StartsWith, SmartListOperator.EndsWith, SmartListOperator.ListContains, SmartListOperator.RegularExpression,
        ],
    };

    public static IEnumerable<object[]> TemplateIds => SmartListTemplateCatalog.All.Select(t => new object[] { t.Id });

    [Fact]
    public void Ids_AreUnique_AndEveryKindHasATemplate()
    {
        Assert.Equal(SmartListTemplateCatalog.All.Count, SmartListTemplateCatalog.All.Select(t => t.Id).Distinct().Count());
        foreach (var kind in Enum.GetValues<SmartListTargetKind>())
        {
            Assert.NotEmpty(SmartListTemplateCatalog.For(kind));
        }
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void EveryRule_UsesAFieldOfItsKind_AndAnOperatorThatFieldSupports(string id)
    {
        var template = SmartListTemplateCatalog.All.Single(t => t.Id == id);
        var definitions = SmartListTemplateCatalog.DefinitionsFor(template.Kind);

        Assert.NotEmpty(template.Rules);
        foreach (var rule in template.Rules)
        {
            Assert.True(definitions.TryGetValue(rule.Field, out var definition), $"{id}: {rule.Field} is not a {template.Kind} field");
            Assert.Contains(rule.Operator, OperatorsFor(definition!.DataType));
            if (rule.Operator is SmartListOperator.InRange or SmartListOperator.DateInRange)
            {
                Assert.False(string.IsNullOrEmpty(rule.Value2), $"{id}: a range rule needs a second value");
            }
        }
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void Instantiate_SavesAndEvaluates_AsAnOrdinaryEditableList(string id)
    {
        var template = SmartListTemplateCatalog.All.Single(t => t.Id == id);

        var list = SmartListTemplateCatalog.Instantiate(template);
        _context.SmartLists.Add(list);
        _context.SaveChanges();

        Assert.False(list.IsSystem);
        Assert.Equal(template.Kind, list.TargetKind);
        Assert.Equal(template.Rules.Count, list.RootGroup.Conditions.Count);
        Assert.Equal(0, SmartListEvaluation.MatchCount(_context, list)); // empty library: evaluates without throwing
    }

    [Fact]
    public void NeverOpened_MatchesOldUnreadIssues_NotRecentOrStartedOnes()
    {
        var series = new Series { Name = "S" };
        series.Issues.Add(new Issue { Number = "1", PageCount = 10, AddedTime = DateTime.Now.AddDays(-200), FilePath = "C:\\x\\1.cbz" });
        series.Issues.Add(new Issue { Number = "2", PageCount = 10, AddedTime = DateTime.Now.AddDays(-5), FilePath = "C:\\x\\2.cbz" });
        series.Issues.Add(new Issue { Number = "3", PageCount = 10, LastPageRead = 4, AddedTime = DateTime.Now.AddDays(-200), FilePath = "C:\\x\\3.cbz" });
        _context.Series.Add(series);
        _context.SaveChanges();

        var list = SmartListTemplateCatalog.Instantiate(SmartListTemplateCatalog.All.Single(t => t.Id == "issue.neverOpened"));

        Assert.Equal(["1"], SmartListQueryBuilder.Build(_context, list).Select(i => i.Number));
    }

    [Fact]
    public void MissingMetadata_IsAnOrList_MatchingAnyOneGap()
    {
        var series = new Series { Name = "S" };
        series.Issues.Add(new Issue { Number = "1", Summary = "text", Year = 2001, Publisher = "Acme", FilePath = "C:\\x\\1.cbz" });
        series.Issues.Add(new Issue { Number = "2", Summary = "text", Year = 2001, FilePath = "C:\\x\\2.cbz" });
        _context.Series.Add(series);
        _context.SaveChanges();

        var list = SmartListTemplateCatalog.Instantiate(SmartListTemplateCatalog.All.Single(t => t.Id == "issue.missingMetadata"));

        Assert.Equal(["2"], SmartListQueryBuilder.Build(_context, list).Select(i => i.Number));
    }
}
