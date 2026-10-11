using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.SmartLists.CeImport;

/// <summary>How a ComicRack matcher's <c>MatchOperator</c> number is read.</summary>
public enum CeMatcherKind
{
    /// <summary><c>ComicBookStringMatcher</c>: 0 is, 1 contains, 2 contains any of, 3 contains all of, 4 starts with, 5 ends with, 6 list contains, 7 regular expression.</summary>
    String,

    /// <summary><c>ComicBookNumericMatcher</c>: 0 is, 1 greater, 2 smaller, 3 in range.</summary>
    Number,

    /// <summary><c>ComicBookDateMatcher</c>: 0 is, 1 after, 2 before, 3 in the last days, 4 in range.</summary>
    Date,

    /// <summary><c>ComicBookCustomValuesMatcher</c>: a string matcher on column 1 (the custom value's name is <c>MatchValue</c>, the value compared <c>MatchValue2</c>).</summary>
    CustomValue,

    /// <summary><c>ComicBookDuplicateMatcher</c>: 0 on, 1 off.</summary>
    Duplicate,
}

/// <summary>
/// The table from a ComicRack matcher's <c>xsi:type</c> to the smart-list field it becomes
/// (docs/superpowers/specs/2026-10-04-smart-list-cbl-import-design.md). Adding a supported matcher is one row; the operator numbers are CE's
/// (<c>ComicBookStringMatcher</c>, <c>ComicBookNumericMatcher</c>, <c>ComicBookDateMatcher</c> in <c>_reference/ComicRackCE/ComicRack.Engine</c>).
/// </summary>
public static class CeMatcherMap
{
    public sealed record Entry(CeMatcherKind Kind, SmartListField Field);

    public const string GroupType = "ComicBookGroupMatcher";
    public const string PluginType = "ComicBookPluginMatcher";

    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal)
    {
        ["ComicBookPublisherMatcher"] = new(CeMatcherKind.String, SmartListField.Publisher),
        ["ComicBookFormatMatcher"] = new(CeMatcherKind.String, SmartListField.Format),
        ["ComicBookTitleMatcher"] = new(CeMatcherKind.String, SmartListField.Title),
        ["ComicBookSeriesMatcher"] = new(CeMatcherKind.String, SmartListField.SeriesName),
        ["ComicBookTagsMatcher"] = new(CeMatcherKind.String, SmartListField.Tags),
        ["ComicBookNotesMatcher"] = new(CeMatcherKind.String, SmartListField.Notes),
        ["ComicBookSummaryMatcher"] = new(CeMatcherKind.String, SmartListField.Summary),
        ["ComicBookMainCharacterOrTeamMatcher"] = new(CeMatcherKind.String, SmartListField.MainCharacterOrTeam),
        ["ComicBookLanguageMatcher"] = new(CeMatcherKind.String, SmartListField.LanguageISO),
        ["ComicBookScanInformationMatcher"] = new(CeMatcherKind.String, SmartListField.ScanInformation),
        ["ComicBookFileMatcher"] = new(CeMatcherKind.String, SmartListField.File),
        ["ComicBookFullPathMatcher"] = new(CeMatcherKind.String, SmartListField.FullPath),
        ["ComicBookDirectoryMatcher"] = new(CeMatcherKind.String, SmartListField.Directory),
        ["ComicBookFileFormatMatcher"] = new(CeMatcherKind.String, SmartListField.FileFormat),

        ["ComicBookCountMatcher"] = new(CeMatcherKind.Number, SmartListField.Count),
        ["ComicBookPageCountMatcher"] = new(CeMatcherKind.Number, SmartListField.PageCount),

        // CE's number matcher parses the issue number to a float; our Number field stays text, so it maps to the numeric view of it.
        ["ComicBookNumberMatcher"] = new(CeMatcherKind.Number, SmartListField.NumberValue),

        // CE's own XmlType for "Series: Last Number" is this misspelled name.
        ["SmartListSeriesMaxNumbertMatcher"] = new(CeMatcherKind.Number, SmartListField.SeriesLastNumber),
        ["SmartListSeriesMaxYearMatcher"] = new(CeMatcherKind.Number, SmartListField.SeriesMaxYear),
        ["SmartListSeriesMaxCountMatcher"] = new(CeMatcherKind.Number, SmartListField.SeriesMaxCount),
        ["SmartListSeriesMaxGapSizeMatcher"] = new(CeMatcherKind.Number, SmartListField.SeriesMaxGapSize),

        ["ComicBookPublishedMatcher"] = new(CeMatcherKind.Date, SmartListField.Published),

        ["ComicBookCustomValuesMatcher"] = new(CeMatcherKind.CustomValue, SmartListField.CustomValue),
        ["ComicBookDuplicateMatcher"] = new(CeMatcherKind.Duplicate, SmartListField.Duplicate),
    };

    public static bool TryGet(string type, out Entry entry) => Entries.TryGetValue(type, out entry!);

    /// <summary>The operator for CE's <paramref name="index"/>, or null when CE has no such operator for the matcher kind.</summary>
    public static SmartListOperator? OperatorFor(CeMatcherKind kind, int index) => kind switch
    {
        CeMatcherKind.String or CeMatcherKind.CustomValue => index switch
        {
            0 => SmartListOperator.Is,
            1 => SmartListOperator.Contains,
            2 => SmartListOperator.ContainsAny,
            3 => SmartListOperator.ContainsAll,
            4 => SmartListOperator.StartsWith,
            5 => SmartListOperator.EndsWith,
            6 => SmartListOperator.ListContains,
            7 => SmartListOperator.RegularExpression,
            _ => null,
        },
        CeMatcherKind.Number => index switch
        {
            0 => SmartListOperator.Is,
            1 => SmartListOperator.GreaterThan,
            2 => SmartListOperator.LessThan,
            3 => SmartListOperator.InRange,
            _ => null,
        },
        CeMatcherKind.Date => index switch
        {
            0 => SmartListOperator.Is,
            1 => SmartListOperator.IsAfter,
            2 => SmartListOperator.IsBefore,
            3 => SmartListOperator.WithinLastDays,
            4 => SmartListOperator.DateInRange,
            _ => null,
        },

        // CE's duplicate matcher with operator 1 ("off") returns every book unchanged rather than the non-duplicates, which has no native spelling; only "on" imports.
        CeMatcherKind.Duplicate => index == 0 ? SmartListOperator.Is : null,
        _ => null,
    };

    /// <summary>The four date-valued series matchers CE has beyond the numeric ones: recognised so the warning can say what they are.</summary>
    public static bool IsKnownUnsupportedSeriesDateMatcher(string type) =>
        type is "SmartListSeriesLastAddedTimeMatcher" or "SmartListSeriesLastOpenedTimeMatcher" or "SmartListSeriesLastPublishedTimeMatcher" or "SmartListSeriesLastReleasedTimeMatcher";
}
