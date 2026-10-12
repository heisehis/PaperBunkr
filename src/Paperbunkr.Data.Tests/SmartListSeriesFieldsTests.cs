using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// The ComicRack-parity smart-list fields (docs/superpowers/specs/2026-10-04-smart-list-cbl-import-design.md): the four "Series: …" figures, the numeric
/// <see cref="SmartListField.NumberValue"/>, <see cref="SmartListField.Published"/>, and a missing custom value comparing as empty. Evaluated over a hand-built
/// snapshot, so no database is involved.
/// </summary>
public class SmartListSeriesFieldsTests
{
    private static Issue Book(string series, string? number, int? year = null, int? count = null, string? volume = null, int? month = null, int? day = null, int id = 0) =>
        new()
        {
            Id = id,
            Series = new Series { Name = series },
            Number = number,
            Year = year,
            Count = count,
            Volume = volume,
            Month = month,
            Day = day,
        };

    private static List<int> Match(IReadOnlyList<Issue> library, SmartListField field, SmartListOperator op, string value, string? value2 = null, bool not = false)
    {
        for (int i = 0; i < library.Count; i++)
        {
            library[i].Id = i + 1;
        }

        var snapshot = new SmartListQueryBuilder.LibrarySnapshot
        {
            Issues = library,
            SeriesStats = SeriesStatistics.Compute(library),
        };
        var list = new SmartList
        {
            Name = "t",
            RootGroup = new SmartListConditionGroup
            {
                Mode = SmartListGroupMode.And,
                Conditions = [new SmartListCondition { Field = field, Operator = op, Value = value, Value2 = value2, Not = not }],
            },
        };
        return SmartListQueryBuilder.Evaluate(snapshot, list).Select(i => i.Id).ToList();
    }

    // --- Series: Last Number ---

    [Fact]
    public void SeriesLastNumber_IsTheHighestNumberOfTheWholeSeries_ForEveryBookOfIt()
    {
        var library = new List<Issue> { Book("A", "1"), Book("A", "2"), Book("A", "25"), Book("B", "3") };

        Assert.Equal([1, 2, 3], Match(library, SmartListField.SeriesLastNumber, SmartListOperator.Is, "25"));
        Assert.Equal([4], Match(library, SmartListField.SeriesLastNumber, SmartListOperator.Is, "3"));
    }

    [Fact]
    public void SeriesLastNumber_InRange_IsInclusive()
    {
        var library = new List<Issue> { Book("A", "1"), Book("A", "2"), Book("B", "25"), Book("C", "26") };

        Assert.Equal([1, 2, 3], Match(library, SmartListField.SeriesLastNumber, SmartListOperator.InRange, "2", "25"));
    }

    [Fact]
    public void SeriesLastNumber_IsMinusOne_WhenNoBookHasANumber()
    {
        var library = new List<Issue> { Book("A", null), Book("A", "Annual") };

        Assert.Equal([1, 2], Match(library, SmartListField.SeriesLastNumber, SmartListOperator.Is, "-1"));
    }

    [Fact]
    public void SeriesStats_GroupBySeriesAndVolume()
    {
        var library = new List<Issue> { Book("A", "1", volume: "1"), Book("A", "9", volume: "2") };

        Assert.Equal([1], Match(library, SmartListField.SeriesLastNumber, SmartListOperator.Is, "1"));
        Assert.Equal([2], Match(library, SmartListField.SeriesLastNumber, SmartListOperator.Is, "9"));
    }

    [Fact]
    public void SeriesStats_AreComputedOverTheWholeLibrary_NotOnlyTheBooksOtherConditionsKept()
    {
        var library = new List<Issue> { Book("A", "1"), Book("A", "30") };
        var snapshot = new SmartListQueryBuilder.LibrarySnapshot { Issues = library, SeriesStats = SeriesStatistics.Compute(library) };
        var list = new SmartList
        {
            Name = "t",
            RootGroup = new SmartListConditionGroup
            {
                Mode = SmartListGroupMode.And,
                Conditions =
                [
                    new SmartListCondition { Field = SmartListField.NumberValue, Operator = SmartListOperator.Is, Value = "1" },
                    new SmartListCondition { Field = SmartListField.SeriesLastNumber, Operator = SmartListOperator.Is, Value = "30" },
                ],
            },
        };

        // Only book "1" passes the first condition, but its series still ends at 30.
        Assert.Single(SmartListQueryBuilder.Evaluate(snapshot, list));
    }

    // --- Series: Last Year / Highest Count ---

    [Fact]
    public void SeriesMaxYear_IsTheLatestYearInTheSeries()
    {
        var library = new List<Issue> { Book("A", "1", year: 2018), Book("A", "2", year: 2021), Book("B", "1", year: 2019), Book("C", "1") };

        Assert.Equal([1, 2], Match(library, SmartListField.SeriesMaxYear, SmartListOperator.GreaterThan, "2020"));
        Assert.Equal([4], Match(library, SmartListField.SeriesMaxYear, SmartListOperator.Is, "-1"));
    }

    [Fact]
    public void SeriesMaxCount_IsTheHighestCountAnyBookOfTheSeriesCarries()
    {
        var library = new List<Issue> { Book("A", "1", count: 12), Book("A", "2", count: 24), Book("B", "1") };

        Assert.Equal([1, 2], Match(library, SmartListField.SeriesMaxCount, SmartListOperator.Is, "24"));
        Assert.Equal([3], Match(library, SmartListField.SeriesMaxCount, SmartListOperator.Is, "-1"));
    }

    // --- Series: Biggest Gap ---

    [Fact]
    public void SeriesMaxGapSize_IsTheLargestStepAboveOne_NotTheCountOfMissingIssues()
    {
        // 1,2,3,6,7 -> one step of 3 (from 3 to 6): CE reports 3 although only two issues are missing.
        var library = new List<Issue> { Book("A", "1"), Book("A", "2"), Book("A", "3"), Book("A", "6"), Book("A", "7") };

        Assert.Equal([1, 2, 3, 4, 5], Match(library, SmartListField.SeriesMaxGapSize, SmartListOperator.Is, "3"));
    }

    [Fact]
    public void SeriesMaxGapSize_IsZero_WhenTheNumbersRunWithoutAJump()
    {
        var library = new List<Issue> { Book("A", "1"), Book("A", "2"), Book("A", "3") };

        Assert.Equal([1, 2, 3], Match(library, SmartListField.SeriesMaxGapSize, SmartListOperator.Is, "0"));
    }

    [Fact]
    public void SeriesMaxGapSize_PicksTheBiggestOfSeveralGaps_AndIgnoresNonNumericNumbers()
    {
        var library = new List<Issue>
        {
            Book("A", "1"), Book("A", "3"), Book("A", "10"), Book("A", "Annual"), Book("A", "11"),
        };

        // steps: 1->3 = 2, 3->10 = 7, 10->11 = 1 -> 7
        Assert.Equal([1, 2, 3, 4, 5], Match(library, SmartListField.SeriesMaxGapSize, SmartListOperator.Is, "7"));
    }

    [Fact]
    public void SeriesMaxGapSize_CountsTheNumbersAfterTheFirstOfARange()
    {
        // CE reads the first number in the text, so "4-6" is 4 (its GetSafeNumber), and the range adds the numbers after the first: 5 and 6.
        var library = new List<Issue> { Book("A", "1"), Book("A", "2"), Book("A", "4-6") };

        // numbers: 1, 2, 4, 5, 6 -> the one step above 1 is 2->4 = 2
        Assert.Equal([1, 2, 3], Match(library, SmartListField.SeriesMaxGapSize, SmartListOperator.Is, "2"));
    }

    // --- Number (numeric) ---

    [Theory]
    [InlineData("5", 5f)]
    [InlineData("5.5", 5.5f)]
    [InlineData("1a", 1f)]
    [InlineData("1/2", 0.5f)]
    [InlineData("Annual", -1f)]
    [InlineData(null, -1f)]
    public void NumberValue_ParsesTheNumberTheWayCeDoes(string? number, float expected)
    {
        float actual = SmartListCatalog.NumberSelectors[SmartListField.NumberValue](Book("A", number));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void NumberValue_InRange_FindsOneShotsByNumberZeroToOne()
    {
        var library = new List<Issue> { Book("A", "0"), Book("B", "1"), Book("C", "2"), Book("D", "TPB") };

        Assert.Equal([1, 2], Match(library, SmartListField.NumberValue, SmartListOperator.InRange, "0", "1"));
    }

    // --- Published ---

    [Fact]
    public void Published_IsBuiltFromYearMonthDay_WithMonthAndDayClamped()
    {
        Assert.Equal(new DateTime(2020, 1, 1), SmartListCatalog.DateSelectors[SmartListField.Published](Book("A", "1", year: 2020)));
        Assert.Equal(new DateTime(2020, 4, 30), SmartListCatalog.DateSelectors[SmartListField.Published](Book("A", "1", year: 2020, month: 4, day: 31)));
        Assert.Equal(new DateTime(2020, 1, 1), SmartListCatalog.DateSelectors[SmartListField.Published](Book("A", "1", year: 2020, month: 0, day: 0)));
        Assert.Equal(new DateTime(2020, 12, 15), SmartListCatalog.DateSelectors[SmartListField.Published](Book("A", "1", year: 2020, month: 13, day: 15)));
    }

    [Fact]
    public void Published_IsUnset_WithoutAPositiveYear()
    {
        Assert.Null(SmartListCatalog.DateSelectors[SmartListField.Published](Book("A", "1")));
        Assert.Null(SmartListCatalog.DateSelectors[SmartListField.Published](Book("A", "1", year: 0)));
    }

    // --- Missing custom value compares as empty (CE parity) ---

    private static List<int> MatchCustom(IReadOnlyList<Issue> library, string name, SmartListOperator op, string value, bool not = false)
    {
        for (int i = 0; i < library.Count; i++)
        {
            library[i].Id = i + 1;
        }

        var snapshot = new SmartListQueryBuilder.LibrarySnapshot { Issues = library };
        var list = new SmartList
        {
            Name = "t",
            RootGroup = new SmartListConditionGroup
            {
                Mode = SmartListGroupMode.And,
                Conditions = [new SmartListCondition { Field = SmartListField.CustomValue, CustomValueName = name, Operator = op, Value = value, Not = not }],
            },
        };
        return SmartListQueryBuilder.Evaluate(snapshot, list).Select(i => i.Id).ToList();
    }

    private static Issue WithCustom(string? name, string? value)
    {
        var issue = Book("A", "1");
        if (name is not null)
        {
            issue.CustomValues.Add(new IssueCustomValue { Name = name, Value = value ?? string.Empty });
        }

        return issue;
    }

    [Fact]
    public void CustomValue_IsEmpty_MatchesBooksThatNeverHadTheValue()
    {
        var library = new List<Issue> { WithCustom(null, null), WithCustom("comicvine_issue", "123"), WithCustom("comicvine_issue", "") };

        Assert.Equal([1, 3], MatchCustom(library, "comicvine_issue", SmartListOperator.Is, ""));
    }

    [Fact]
    public void CustomValue_NotEmpty_ExcludesBooksThatNeverHadTheValue()
    {
        var library = new List<Issue> { WithCustom(null, null), WithCustom("comicvine_issue", "123") };

        Assert.Equal([2], MatchCustom(library, "comicvine_issue", SmartListOperator.Is, "", not: true));
    }

    [Fact]
    public void CustomValue_IsNotX_MatchesBooksThatNeverHadTheValue()
    {
        var library = new List<Issue> { WithCustom(null, null), WithCustom("Universe", "Ultimate"), WithCustom("Universe", "Noir") };

        Assert.Equal([1, 3], MatchCustom(library, "Universe", SmartListOperator.Is, "Ultimate", not: true));
    }

    [Fact]
    public void CustomValue_IsX_StillOnlyMatchesBooksThatHaveIt()
    {
        var library = new List<Issue> { WithCustom(null, null), WithCustom("Universe", "Ultimate") };

        Assert.Equal([2], MatchCustom(library, "Universe", SmartListOperator.Is, "Ultimate"));
    }
}
