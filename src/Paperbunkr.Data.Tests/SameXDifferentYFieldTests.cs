using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists;
using Paperbunkr.Data.SmartLists.CeImport;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// The <see cref="SmartListField.SameXDifferentY"/> condition inside a smart list, and how the ComicRack script maps onto it
/// (docs/superpowers/specs/2026-10-11-series-checks-and-bulk-fixes-design.md §1).
/// </summary>
public class SameXDifferentYFieldTests
{
    private const string Header = "<?xml version=\"1.0\"?><ReadingList xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">";

    private static Issue Book(string series, string number, string? format, params (string Name, string Value)[] custom)
    {
        var issue = new Issue { Series = new Series { Name = series }, Number = number, Format = format };
        foreach ((string name, string value) in custom)
        {
            issue.CustomValues.Add(new IssueCustomValue { Name = name, Value = value });
        }

        return issue;
    }

    private static SmartListCondition SameX(string x, string y, SmartListOperator op = SmartListOperator.Is, bool not = false) =>
        new() { Field = SmartListField.SameXDifferentY, Operator = op, Value = x, Value2 = y, Not = not };

    private static List<string> Match(IReadOnlyList<Issue> library, params SmartListCondition[] conditions)
    {
        for (int i = 0; i < library.Count; i++)
        {
            library[i].Id = i + 1;
        }

        var list = new SmartList { Name = "t", RootGroup = new SmartListConditionGroup { Mode = SmartListGroupMode.And, Conditions = conditions.ToList() } };
        var snapshot = new SmartListQueryBuilder.LibrarySnapshot { Issues = library };
        return SmartListQueryBuilder.Evaluate(snapshot, list).Select(i => $"{i.Series!.Name}#{i.Number}").ToList();
    }

    [Fact]
    public void ItFlagsTheBooksOfAGroupWhoseYDiffers()
    {
        var library = new List<Issue> { Book("A", "1", "TPB"), Book("A", "2", "Annual"), Book("B", "1", "TPB"), Book("B", "2", "TPB") };

        Assert.Equal(["A#1", "A#2"], Match(library, SameX("Series", "Format")));
    }

    [Fact]
    public void WithoutY_ItIsADuplicateFinder()
    {
        var library = new List<Issue> { Book("A", "1", "TPB"), Book("A", "1", "TPB"), Book("A", "2", "TPB") };

        Assert.Equal(["A#1", "A#1"], Match(library, SameX("Series|Number|Format", string.Empty)));
    }

    [Fact]
    public void IsNotAndTheNotFlag_FindTheOthers()
    {
        var library = new List<Issue> { Book("A", "1", "TPB"), Book("A", "2", "Annual"), Book("B", "1", "TPB") };

        Assert.Equal(["B#1"], Match(library, SameX("Series", "Format", SmartListOperator.IsNot)));
        Assert.Equal(["B#1"], Match(library, SameX("Series", "Format", not: true)));
    }

    [Fact]
    public void ItCombinesWithTheOtherConditionsOfTheList_AsAnAndOverTheWholeLibraryGroups()
    {
        // The shape of "7.1 Dupe with Comicvine_issue": the groups are found over the whole library, then the other rule filters.
        var processed = Book("A", "1", "TPB", ("comicvine_issue", "9"), ("datamanager_processed", "yes"));
        var unprocessed = Book("A", "1", "TPB", ("comicvine_issue", "9"));
        var other = Book("B", "1", "TPB", ("comicvine_issue", "5"), ("datamanager_processed", "yes"));
        var custom = new SmartListCondition { Field = SmartListField.CustomValue, CustomValueName = "datamanager_processed", Operator = SmartListOperator.Is, Value = "yes" };

        var matched = Match([processed, unprocessed, other], SameX("comicvine_issue|Format", string.Empty), custom);

        Assert.Equal(["A#1"], matched);
    }

    [Fact]
    public void TwoConditionsOfOneList_KeepTheirOwnGroups()
    {
        var library = new List<Issue> { Book("A", "1", "TPB"), Book("A", "2", "TPB"), Book("B", "1", "Annual"), Book("C", "1", "Annual") };

        Assert.Equal(["A#1", "A#2"], Match(library, SameX("Series", string.Empty), SameX("Format", string.Empty)));
        Assert.Equal(["A#1", "A#2", "B#1", "C#1"], Match(library, SameX("Format", string.Empty)));
    }

    // --- Import ---

    private static CeSmartListDraft Read(string matchers) =>
        new CeSmartListReader().Read(new MemoryStream(System.Text.Encoding.UTF8.GetBytes($"{Header}<Name>t</Name><Books /><Matchers>{matchers}</Matchers></ReadingList>")), "t");

    private const string Script = "<ComicBookMatcher xsi:type=\"ComicBookPluginMatcher\" MatchOperator=\"2\" PluginKey=\"SameXDifferentY\"><MatchValue>comicvine_volume</MatchValue><MatchValue2>Format</MatchValue2></ComicBookMatcher>";
    private const string Filter = "<ComicBookMatcher xsi:type=\"ComicBookFormatMatcher\"><MatchValue>TPB</MatchValue></ComicBookMatcher>";

    [Fact]
    public void TheScript_AsTheFirstRule_ImportsCleanly_WithItsTwoArguments()
    {
        var draft = Read(Script + Filter);

        Assert.Empty(draft.Warnings);
        var condition = draft.List.RootGroup.Conditions.Single(c => c.Field == SmartListField.SameXDifferentY);
        Assert.Equal("comicvine_volume", condition.Value);
        Assert.Equal("Format", condition.Value2);
    }

    [Fact]
    public void TheScript_AfterOtherRules_ImportsWithAnApproximationWarning()
    {
        var draft = Read(Filter + Script);

        Assert.Single(draft.List.RootGroup.Conditions.Where(c => c.Field == SmartListField.SameXDifferentY));
        var warning = Assert.Single(draft.Warnings);
        Assert.Equal(CeWarningEffect.Approximated, warning.Effect);
        Assert.Contains("whole library", warning.Detail);
        Assert.Contains("may differ", warning.ToString());
    }

    [Fact]
    public void TheScript_FirstInsideAGroupThatIsNotFirst_IsApproximatedToo()
    {
        string group = $"<ComicBookMatcher xsi:type=\"ComicBookGroupMatcher\" MatcherMode=\"Or\"><Matchers>{Script}</Matchers></ComicBookMatcher>";

        Assert.Single(Read(Filter + group).Warnings);
        Assert.Empty(Read(group + Filter).Warnings);
    }

    [Fact]
    public void ANotOnTheScript_ReachesTheCondition()
    {
        var draft = Read(Script.Replace("PluginKey=\"SameXDifferentY\"", "PluginKey=\"SameXDifferentY\" Not=\"true\""));

        Assert.True(draft.List.RootGroup.Conditions.Single().Not);
    }
}
