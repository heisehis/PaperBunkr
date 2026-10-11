using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists;
using Paperbunkr.Data.SmartLists.CeImport;

namespace Paperbunkr.Data.Tests.CeSmartLists;

/// <summary>
/// <see cref="CeSmartListReader"/> against real ComicRack smart-list files (<c>TestData\CeSmartLists</c>, a user's workflow lists) and small inline documents
/// (docs/superpowers/specs/2026-10-04-smart-list-cbl-import-design.md).
/// </summary>
public class CeSmartListReaderTests
{
    private const string Header = "<?xml version=\"1.0\"?><ReadingList xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"";

    private static CeSmartListDraft ReadFixture(string name, PluginMatcherRegistry? plugins = null)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "CeSmartLists", name + ".cbl"));
        return new CeSmartListReader(plugins ?? new PluginMatcherRegistry()).Read(stream, name);
    }

    private static CeSmartListDraft ReadXml(string matchers, string rootAttributes = "", string extraRootElements = "") =>
        new CeSmartListReader(new PluginMatcherRegistry()).Read(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes($"{Header} {rootAttributes}><Name>t</Name><Books />{extraRootElements}<Matchers>{matchers}</Matchers></ReadingList>")), "fallback");

    private static string Leaf(string type, string value, int op = 0, bool not = false, string? value2 = null, string extra = "") =>
        $"<ComicBookMatcher xsi:type=\"{type}\" MatchOperator=\"{op}\"{(not ? " Not=\"true\"" : "")}{extra}><MatchValue>{value}</MatchValue>{(value2 is null ? "" : $"<MatchValue2>{value2}</MatchValue2>")}</ComicBookMatcher>";

    private static SmartListCondition Single(CeSmartListDraft draft) => Assert.Single(draft.List.RootGroup.Conditions);

    // --- Real files ---

    [Fact]
    public void RealFile_KeepsTheListName()
    {
        Assert.Equal("7.3 Duplicate Script", ReadFixture("7.3 Duplicate Script").Name);
    }

    [Fact]
    public void RealFile_Duplicate_On_BecomesTheDuplicateField()
    {
        var condition = Single(ReadFixture("7.3 Duplicate Script"));

        Assert.Equal(SmartListField.Duplicate, condition.Field);
        Assert.Equal(SmartListOperator.Is, condition.Operator);
        Assert.False(condition.Not);
    }

    [Fact]
    public void RealFile_CustomValue_TakesItsNameFromMatchValueAndItsValueFromMatchValue2()
    {
        var draft = ReadFixture("8.6 Alternate Universes now has a Main");

        var first = draft.List.RootGroup.Conditions[0];
        Assert.Equal(SmartListField.CustomValue, first.Field);
        Assert.Equal("DataManager_processed", first.CustomValueName);
        Assert.Equal("yes", first.Value);
        Assert.Equal(SmartListOperator.Is, first.Operator);
        Assert.False(first.Not);

        var second = draft.List.RootGroup.Conditions[1];
        Assert.Equal("comicvine_issue", second.CustomValueName);
        Assert.Equal(string.Empty, second.Value);
        Assert.True(second.Not);
    }

    [Fact]
    public void RealFile_TopLevelMatchers_AreAnAndGroup_InFileOrder()
    {
        var draft = ReadFixture("8.6 Alternate Universes now has a Main");

        Assert.Equal(SmartListGroupMode.And, draft.List.RootGroup.Mode);
        Assert.Equal(
            [SmartListField.CustomValue, SmartListField.CustomValue, SmartListField.Format, SmartListField.FullPath, SmartListField.MainCharacterOrTeam],
            draft.List.RootGroup.Conditions.OrderBy(c => c.SortOrder).Select(c => c.Field));
        Assert.Empty(draft.Warnings);
    }

    [Fact]
    public void RealFile_StringOperatorIndex_BecomesTheOperator()
    {
        var draft = ReadFixture("8.6 Alternate Universes now has a Main");

        var fullPath = draft.List.RootGroup.Conditions.Single(c => c.Field == SmartListField.FullPath);
        Assert.Equal(SmartListOperator.ContainsAny, fullPath.Operator);
        Assert.Equal("Non Universe|Alternate Universes", fullPath.Value);
    }

    [Fact]
    public void RealFile_OrGroup_BecomesAnOrChildGroup()
    {
        var draft = ReadFixture("3.7 Process Skipped");

        Assert.Empty(draft.List.RootGroup.Conditions);
        var group = Assert.Single(draft.List.RootGroup.ChildGroups);
        Assert.Equal(SmartListGroupMode.Or, group.Mode);
        var notes = Assert.Single(group.Conditions);
        Assert.Equal(SmartListField.Notes, notes.Field);
        Assert.Equal(SmartListOperator.Contains, notes.Operator);
        Assert.Equal("CVDBSKIP", notes.Value);
    }

    [Fact]
    public void RealFile_SeriesLastNumber_InRange_KeepsBothBounds_AndTheNestedGroupKeepsItsMode()
    {
        var draft = ReadFixture("6.8 Set Format Limited Series");

        var outer = Assert.Single(draft.List.RootGroup.ChildGroups);
        Assert.Equal(SmartListGroupMode.And, outer.Mode);
        Assert.All(outer.Conditions.Where(c => c.Field == SmartListField.Format), c => Assert.True(c.Not));
        var inner = Assert.Single(outer.ChildGroups);
        Assert.Equal(SmartListGroupMode.Or, inner.Mode);
        var last = Assert.Single(inner.Conditions);
        Assert.Equal(SmartListField.SeriesLastNumber, last.Field);
        Assert.Equal(SmartListOperator.InRange, last.Operator);
        Assert.Equal("2", last.Value);
        Assert.Equal("25", last.Value2);
    }

    [Fact]
    public void RealFile_RootMatcherModeOr_IsKept()
    {
        // "6.6.1 Set Format TPB" has MatcherMode="Or" on <ReadingList> itself.
        var draft = ReadFixture("6.6.1 Set Format TPB (double check)");

        Assert.Equal(SmartListGroupMode.Or, draft.List.RootGroup.Mode);
    }

    [Fact]
    public void RealFile_NumberMatcher_BecomesTheNumericNumberField()
    {
        var draft = ReadFixture("6.6.1 Set Format TPB (double check)");

        var number = draft.List.RootGroup.ChildGroups.Single().Conditions.Single(c => c.Field == SmartListField.NumberValue);
        Assert.Equal(SmartListOperator.Is, number.Operator);
        Assert.Equal("1", number.Value);
    }

    [Fact]
    public void RealFile_EmptyCountMatcher_MeansCountUnset_SoItIsMinusOne()
    {
        var draft = ReadFixture("4.4 Add Count - Missing");

        var count = draft.List.RootGroup.Conditions.Single(c => c.Field == SmartListField.Count);
        Assert.Equal(SmartListOperator.Is, count.Operator);
        Assert.Equal("-1", count.Value);
    }

    [Fact]
    public void RealFile_PublishedIsEmpty_MeansNoPublishedDate_SoItReadsTheYear()
    {
        var draft = ReadFixture("4.3 Add Count - Missing published date");

        var published = draft.List.RootGroup.Conditions.Single(c => c.Field == SmartListField.Year);
        Assert.Equal(SmartListOperator.LessThan, published.Operator);
        Assert.Equal("1", published.Value);
        Assert.Empty(draft.Warnings);
    }

    [Fact]
    public void RealFile_SeriesMaxYear_KeepsItsOperator()
    {
        var draft = ReadFixture("4.2 Add Count - Ongoing");

        var year = draft.List.RootGroup.Conditions.Single(c => c.Field == SmartListField.SeriesMaxYear);
        Assert.Equal(SmartListField.SeriesMaxYear, year.Field);
        Assert.Equal(SmartListOperator.Is, year.Operator);
        Assert.Equal("2020", year.Value);
    }

    [Theory]
    [InlineData("2.6 Convert to CBZ", SmartListField.FileFormat)]
    [InlineData("1.6 Remove Language", SmartListField.LanguageISO)]
    [InlineData("1.9 Add Scanner Info", SmartListField.ScanInformation)]
    [InlineData("2.1 Page count to low - Delete", SmartListField.PageCount)]
    [InlineData("1.1 Missing - Comicvine_issue", SmartListField.FullPath)]
    [InlineData("2.3 ComicVine Scrapper TPB", SmartListField.File)]
    [InlineData("2.2.4 TPB", SmartListField.Directory)]
    [InlineData("6.3.1 Rules_ TPB", SmartListField.Summary)]
    [InlineData("1.1 Non Universe sort Marvel", SmartListField.Tags)]
    public void RealFile_EachMatcherTypeMapsToItsField(string file, SmartListField expected)
    {
        var draft = ReadFixture(file);

        Assert.Contains(draft.List.RootGroup.Conditions.Concat(draft.List.RootGroup.ChildGroups.SelectMany(g => g.Conditions)), c => c.Field == expected);
    }

    [Fact]
    public void RealFile_SummaryRegexOperator_IsRegularExpression()
    {
        var draft = ReadFixture("6.3.1 Rules_ TPB");

        var summary = draft.List.RootGroup.ChildGroups.SelectMany(g => g.Conditions).Single(c => c.Field == SmartListField.Summary);
        Assert.Equal(SmartListOperator.RegularExpression, summary.Operator);
        Assert.Equal("#[0-9]-[0-9]", summary.Value);
    }

    // --- Plugin matchers ---

    [Fact]
    public void PluginMatcher_WithNoEquivalent_IsDroppedAndNamed()
    {
        var draft = ReadFixture("1.1 Commit Proposed Values");

        Assert.True(draft.IsEmpty);
        var warning = Assert.Single(draft.Warnings);
        Assert.Equal("script 'findProposedValues'", warning.ConditionType);
        Assert.Equal(CeWarningEffect.Widened, warning.Effect);
        Assert.Equal("1.1 Commit Proposed Values", warning.ListName);
    }

    [Fact]
    public void PluginMatcher_DroppedFromAnAndList_KeepsTheRestAndSaysTheListWidened()
    {
        var draft = ReadFixture("7.1 Dupe with Comicvine_issue");

        Assert.False(draft.IsEmpty);
        var kept = Single(draft);
        Assert.Equal(SmartListField.CustomValue, kept.Field);
        Assert.Equal("datamanager_processed", kept.CustomValueName);
        Assert.Equal("yes", kept.Value);
        var warning = Assert.Single(draft.Warnings);
        Assert.Equal("script 'SameXDifferentY'", warning.ConditionType);
        Assert.Equal(CeWarningEffect.Widened, warning.Effect);
    }

    [Fact]
    public void PluginMatcher_WithARegisteredEquivalent_BecomesThatCondition_AndKeepsItsNot()
    {
        var registry = new PluginMatcherRegistry();
        registry.Register("findProposedValues", (value, _) => new SmartListCondition { Field = SmartListField.Title, Operator = SmartListOperator.Is, Value = "got:" + value });
        var matcher = "<ComicBookMatcher xsi:type=\"ComicBookPluginMatcher\" MatchOperator=\"1\" PluginKey=\"findProposedValues\" Not=\"true\"><MatchValue>year</MatchValue></ComicBookMatcher>";

        var draft = new CeSmartListReader(registry).Read(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes($"{Header}><Name>t</Name><Books /><Matchers>{matcher}</Matchers></ReadingList>")), "f");

        var condition = Single(draft);
        Assert.Equal("got:year", condition.Value);
        Assert.True(condition.Not);
        Assert.Empty(draft.Warnings);
    }

    [Fact]
    public void PluginMatcher_TheOperatorNumberIsIgnored()
    {
        var registry = new PluginMatcherRegistry();
        registry.Register("k", (_, _) => new SmartListCondition { Field = SmartListField.Title, Operator = SmartListOperator.Is, Value = "x" });

        foreach (int op in new[] { 0, 1, 2, 3, 9 })
        {
            var matcher = $"<ComicBookMatcher xsi:type=\"ComicBookPluginMatcher\" MatchOperator=\"{op}\" PluginKey=\"k\"><MatchValue /></ComicBookMatcher>";
            var draft = new CeSmartListReader(registry).Read(
                new MemoryStream(System.Text.Encoding.UTF8.GetBytes($"{Header}><Name>t</Name><Books /><Matchers>{matcher}</Matchers></ReadingList>")), "f");
            Assert.Single(draft.List.RootGroup.Conditions);
        }
    }

    [Fact]
    public void PluginMatcher_WithoutAKey_IsDropped()
    {
        var draft = ReadXml("<ComicBookMatcher xsi:type=\"ComicBookPluginMatcher\" MatchOperator=\"0\"><MatchValue /></ComicBookMatcher>" + Leaf("ComicBookFormatMatcher", "X"));

        Assert.Equal("script matcher", Assert.Single(draft.Warnings).ConditionType);
        Assert.Single(draft.List.RootGroup.Conditions);
    }

    // --- Mapping details ---

    [Theory]
    [InlineData(0, SmartListOperator.Is)]
    [InlineData(1, SmartListOperator.Contains)]
    [InlineData(2, SmartListOperator.ContainsAny)]
    [InlineData(3, SmartListOperator.ContainsAll)]
    [InlineData(4, SmartListOperator.StartsWith)]
    [InlineData(5, SmartListOperator.EndsWith)]
    [InlineData(6, SmartListOperator.ListContains)]
    [InlineData(7, SmartListOperator.RegularExpression)]
    public void StringOperators_FollowCesNumbering(int index, SmartListOperator expected)
    {
        Assert.Equal(expected, Single(ReadXml(Leaf("ComicBookTitleMatcher", "x", index))).Operator);
    }

    [Theory]
    [InlineData(0, SmartListOperator.Is)]
    [InlineData(1, SmartListOperator.GreaterThan)]
    [InlineData(2, SmartListOperator.LessThan)]
    [InlineData(3, SmartListOperator.InRange)]
    public void NumericOperators_FollowCesNumbering(int index, SmartListOperator expected)
    {
        var condition = Single(ReadXml(Leaf("ComicBookCountMatcher", "5", index, value2: "9")));

        Assert.Equal(expected, condition.Operator);
        Assert.Equal(expected == SmartListOperator.InRange ? "9" : null, condition.Value2);
    }

    [Theory]
    [InlineData(0, SmartListOperator.Is)]
    [InlineData(1, SmartListOperator.IsAfter)]
    [InlineData(2, SmartListOperator.IsBefore)]
    [InlineData(3, SmartListOperator.WithinLastDays)]
    [InlineData(4, SmartListOperator.DateInRange)]
    public void DateOperators_FollowCesNumbering(int index, SmartListOperator expected)
    {
        var condition = Single(ReadXml(Leaf("ComicBookPublishedMatcher", "2020-01-02", index, value2: "2021-01-02")));

        Assert.Equal(SmartListField.Published, condition.Field);
        Assert.Equal(expected, condition.Operator);
        Assert.Equal(expected == SmartListOperator.DateInRange ? "2021-01-02" : null, condition.Value2);
    }

    [Fact]
    public void AnOperatorNumberCesTableDoesNotHave_DropsTheCondition()
    {
        var draft = ReadXml(Leaf("ComicBookTitleMatcher", "x", 9) + Leaf("ComicBookFormatMatcher", "y"));

        Assert.Single(draft.List.RootGroup.Conditions);
        Assert.Contains("operator 9", Assert.Single(draft.Warnings).Detail);
    }

    [Fact]
    public void DuplicateOff_HasNoNativeSpelling_AndIsDropped()
    {
        var draft = ReadXml(Leaf("ComicBookDuplicateMatcher", "", 1));

        Assert.True(draft.IsEmpty);
        Assert.Single(draft.Warnings);
    }

    [Fact]
    public void NotAndIgnoreCase_AreKept()
    {
        var condition = Single(ReadXml(Leaf("ComicBookTitleMatcher", "x", not: true, extra: " IgnoreCase=\"false\"")));

        Assert.True(condition.Not);
        Assert.False(condition.IgnoreCase);
        Assert.True(Single(ReadXml(Leaf("ComicBookTitleMatcher", "x"))).IgnoreCase);
    }

    [Fact]
    public void MatchValue2_OnASingleArgumentMatcher_IsJunkAndIgnored()
    {
        var condition = Single(ReadXml(Leaf("ComicBookNotesMatcher", "Issue ID", 1, value2: "yes")));

        Assert.Equal("Issue ID", condition.Value);
        Assert.Null(condition.Value2);
    }

    [Fact]
    public void ANumberItCannotParse_BecomesMinusOne_LikeCe()
    {
        Assert.Equal("-1", Single(ReadXml(Leaf("ComicBookCountMatcher", "abc"))).Value);
        Assert.Equal("5.5", Single(ReadXml(Leaf("ComicBookCountMatcher", "5.5"))).Value);
    }

    [Fact]
    public void SeriesMaxCountAndGapSize_MapToTheirFields()
    {
        var draft = ReadXml(Leaf("SmartListSeriesMaxCountMatcher", "12", 1) + Leaf("SmartListSeriesMaxGapSizeMatcher", "2", 2));

        Assert.Equal([SmartListField.SeriesMaxCount, SmartListField.SeriesMaxGapSize], draft.List.RootGroup.Conditions.OrderBy(c => c.SortOrder).Select(c => c.Field));
    }

    // --- Unsupported things ---

    [Fact]
    public void AnUnknownMatcherType_IsDroppedWithAWarning()
    {
        var draft = ReadXml(Leaf("ComicBookMysteryMatcher", "x") + Leaf("ComicBookFormatMatcher", "y"));

        var warning = Assert.Single(draft.Warnings);
        Assert.Equal("Mystery", warning.ConditionType);
        Assert.Single(draft.List.RootGroup.Conditions);
    }

    [Fact]
    public void ASeriesDateMatcher_IsRecognisedByName()
    {
        var draft = ReadXml(Leaf("SmartListSeriesLastAddedTimeMatcher", "2020-01-01", 1) + Leaf("ComicBookFormatMatcher", "y"));

        Assert.Contains("series date matcher", Assert.Single(draft.Warnings).Detail);
    }

    [Fact]
    public void ADroppedConditionInAnOrGroup_SaysTheListNarrowed()
    {
        var draft = ReadXml(Leaf("ComicBookMysteryMatcher", "x") + Leaf("ComicBookFormatMatcher", "y"), rootAttributes: "MatcherMode=\"Or\"");

        Assert.Equal(CeWarningEffect.Narrowed, Assert.Single(draft.Warnings).Effect);
    }

    [Fact]
    public void AGroupWhoseEveryConditionIsDropped_IsDroppedWholeNotLeftEmpty()
    {
        string group = "<ComicBookMatcher xsi:type=\"ComicBookGroupMatcher\" MatcherMode=\"Or\"><Matchers>" + Leaf("ComicBookMysteryMatcher", "x") + "</Matchers></ComicBookMatcher>";

        var draft = ReadXml(group + Leaf("ComicBookFormatMatcher", "y"));

        Assert.Empty(draft.List.RootGroup.ChildGroups);
        Assert.Single(draft.List.RootGroup.Conditions);
        Assert.Equal(2, draft.Warnings.Count);
    }

    [Fact]
    public void AFileWhoseEveryConditionIsDropped_IsFlaggedEmpty()
    {
        Assert.True(ReadXml(Leaf("ComicBookMysteryMatcher", "x")).IsEmpty);
        Assert.False(ReadXml(Leaf("ComicBookFormatMatcher", "x")).IsEmpty);
        Assert.False(ReadXml(string.Empty).IsEmpty);
    }

    [Fact]
    public void LimitAndBaseList_AreReported_SinceTheyNarrowTheListBeyondItsMatchers()
    {
        var draft = ReadXml(Leaf("ComicBookFormatMatcher", "x"), extraRootElements: $"<Limit>true</Limit><BaseListId>{Guid.NewGuid()}</BaseListId>");

        Assert.Equal(["limit", "base list"], draft.Warnings.Select(w => w.ConditionType));
        Assert.All(draft.Warnings, w => Assert.Equal(CeWarningEffect.Widened, w.Effect));
    }

    [Fact]
    public void AnEmptyBaseList_IsNotAnOption()
    {
        var draft = ReadXml(Leaf("ComicBookFormatMatcher", "x"), extraRootElements: $"<Limit>false</Limit><BaseListId>{Guid.Empty}</BaseListId>");

        Assert.Empty(draft.Warnings);
    }

    [Fact]
    public void NotAReadingList_Throws()
    {
        Assert.Throws<InvalidDataException>(() => new CeSmartListReader().Read(new MemoryStream("<Other/>"u8.ToArray()), "x"));
        Assert.Throws<InvalidDataException>(() => new CeSmartListReader().Read(new MemoryStream("not xml"u8.ToArray()), "x"));
    }

    [Fact]
    public void ANamelessList_UsesTheFallbackName()
    {
        var draft = new CeSmartListReader().Read(new MemoryStream(System.Text.Encoding.UTF8.GetBytes($"{Header}><Books /><Matchers /></ReadingList>")), "from-file");

        Assert.Equal("from-file", draft.Name);
        Assert.Equal("from-file", draft.List.Name);
    }

    [Fact]
    public void TheDraft_IsAnIssueList_NotASystemList()
    {
        var draft = ReadXml(Leaf("ComicBookFormatMatcher", "x"));

        Assert.Equal(SmartListTargetKind.Issue, draft.List.TargetKind);
        Assert.False(draft.List.IsSystem);
    }

    // --- Group negation (De Morgan), checked against every truth assignment ---

    private static Issue Book(string format, string publisher, string title) =>
        new() { Series = new Series { Name = "S" }, Format = format, Publisher = publisher, Title = title };

    private static bool[] Evaluate(SmartList list, IReadOnlyList<Issue> books)
    {
        for (int i = 0; i < books.Count; i++)
        {
            books[i].Id = i + 1;
        }

        var snapshot = new SmartListQueryBuilder.LibrarySnapshot { Issues = books };
        var matched = SmartListQueryBuilder.Evaluate(snapshot, list).Select(b => b.Id).ToHashSet();
        return books.Select(b => matched.Contains(b.Id)).ToArray();
    }

    // Eight books: every combination of (format == F, publisher == P, title == T).
    private static List<Issue> TruthTable() =>
        Enumerable.Range(0, 8).Select(bits => Book((bits & 1) != 0 ? "F" : "other-f", (bits & 2) != 0 ? "P" : "other-p", (bits & 4) != 0 ? "T" : "other-t")).ToList();

    [Theory]
    [InlineData("And", false)]
    [InlineData("Or", false)]
    [InlineData("And", true)]
    [InlineData("Or", true)]
    public void AGroupWithNot_MatchesTheComplementOfTheSameGroupWithout(string mode, bool innerNot)
    {
        string inner = Leaf("ComicBookFormatMatcher", "F") + Leaf("ComicBookPublisherMatcher", "P", not: innerNot) + Leaf("ComicBookTitleMatcher", "T");
        string plain = $"<ComicBookMatcher xsi:type=\"ComicBookGroupMatcher\" MatcherMode=\"{mode}\"><Matchers>{inner}</Matchers></ComicBookMatcher>";
        string negated = plain.Replace("ComicBookGroupMatcher\"", "ComicBookGroupMatcher\" Not=\"true\"");
        var books = TruthTable();

        bool[] without = Evaluate(ReadXml(plain).List, books);
        bool[] with = Evaluate(ReadXml(negated).List, books);

        Assert.Equal(without.Select(b => !b), with);
    }

    [Fact]
    public void ANegatedGroup_NestedInANegatedGroup_CancelsOut()
    {
        string leafGroup = $"<ComicBookMatcher xsi:type=\"ComicBookGroupMatcher\" MatcherMode=\"Or\" Not=\"true\"><Matchers>{Leaf("ComicBookFormatMatcher", "F")}{Leaf("ComicBookPublisherMatcher", "P")}</Matchers></ComicBookMatcher>";
        string outer = $"<ComicBookMatcher xsi:type=\"ComicBookGroupMatcher\" MatcherMode=\"And\" Not=\"true\"><Matchers>{leafGroup}{Leaf("ComicBookTitleMatcher", "T")}</Matchers></ComicBookMatcher>";
        var books = TruthTable();

        bool[] actual = Evaluate(ReadXml(outer).List, books);

        // NOT( NOT(F or P) and T ) per book
        bool[] expected = books.Select(b => !(!(b.Format == "F" || b.Publisher == "P") && b.Title == "T")).ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void AnEmptyNegatedGroup_MatchesNothing_LikeCe()
    {
        string empty = "<ComicBookMatcher xsi:type=\"ComicBookGroupMatcher\" Not=\"true\"><Matchers /></ComicBookMatcher>";

        bool[] matched = Evaluate(ReadXml(empty).List, TruthTable());

        Assert.All(matched, m => Assert.False(m));
    }

    [Fact]
    public void ADroppedConditionInANegatedAndGroup_IsWordedByTheFlippedMode()
    {
        string group = $"<ComicBookMatcher xsi:type=\"ComicBookGroupMatcher\" MatcherMode=\"And\" Not=\"true\"><Matchers>{Leaf("ComicBookMysteryMatcher", "x")}{Leaf("ComicBookFormatMatcher", "y")}</Matchers></ComicBookMatcher>";

        var draft = ReadXml(group);

        // NOT(A and B) is (NOT A) or (NOT B): losing a term of an OR narrows the list.
        Assert.Equal(CeWarningEffect.Narrowed, Assert.Single(draft.Warnings).Effect);
    }

    [Fact]
    public void ARootOrList_MatchesAnyOfItsTopLevelMatchers()
    {
        var draft = ReadXml(Leaf("ComicBookFormatMatcher", "F") + Leaf("ComicBookPublisherMatcher", "P"), rootAttributes: "MatcherMode=\"Or\"");
        var books = TruthTable();

        bool[] actual = Evaluate(draft.List, books);

        Assert.Equal(books.Select(b => b.Format == "F" || b.Publisher == "P").ToArray(), actual);
    }
}
