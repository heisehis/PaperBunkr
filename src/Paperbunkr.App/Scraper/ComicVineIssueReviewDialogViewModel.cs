using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.ComicVine.Scraping;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.Scraper;

/// <summary>One issue row (docs/superpowers/specs/2026-09-13-cluster-scraper-ui-redesign-design.md
/// §3) - no score/tiering, unlike <see cref="ComicVineMatchCandidateViewModel"/>: issues within one
/// already-chosen volume aren't ranked against each other, just listed.</summary>
public sealed partial class ComicVineIssueCandidateViewModel : ObservableObject
{
    private readonly Action<ComicVineIssueCandidateViewModel> _select;

    public ComicVineIssueSummary Issue { get; }

    [ObservableProperty]
    private bool _isSelected;

    public string DisplayLabel => $"#{Issue.IssueNumber ?? "?"} - {Issue.Name ?? "Untitled"}";

    public string? CoverImageUrl => Issue.ImageUrl;

    /// <summary>A single sortable scalar collapsing <see cref="ComicVineIssueReviewDialogViewModel.NaturalKey"/>'s
    /// 3-part tuple into one ordinal-string-sortable value (docs/superpowers/specs/2026-09-24-scraper-
    /// review-tables-and-batch-summary-design.md §1.2) - DataGrid's own sort only supports a single
    /// <c>SortMemberPath</c> redirect, not a custom multi-key comparer (confirmed Avalonia
    /// limitation), so the Issue# column's display (<see cref="ComicVineIssueSummary.IssueNumber"/>)
    /// and its sort key are two different bindings on the same template column. The delimiter is
    /// U+0001 (a control character, never a realistic character in a series/issue string) specifically
    /// because its ordinal value is lower than every printable character a real prefix/suffix could
    /// contain - a "|" (U+007C) delimiter was tried first and is wrong: it sorts *after* lowercase
    /// letters, so an empty prefix (ordinal-shorter) would incorrectly compare as *greater than* a
    /// non-empty one like "annual" at the very first differing character, inverting the ordering
    /// <see cref="ComicVineIssueReviewDialogViewModel.NaturalKeyComparer"/> actually wants (caught by
    /// this class's own test for "3" vs "Annual 1"). The number segment is offset then zero-padded so
    /// it string-sorts identically to the comparer's own numeric comparison, and a single leading space
    /// stands in for "no number at all" so it still sorts before any digit, matching
    /// <c>Nullable.Compare</c>'s own "null sorts first" the comparer relies on.</summary>
    public string NaturalSortKey
    {
        get
        {
            var key = ComicVineIssueReviewDialogViewModel.NaturalKey(Issue.IssueNumber);
            const string delimiter = "";
            const double numberOffset = 1_000_000; // comfortably covers any realistic issue number, including CE's own negative-number regex allowance
            string numberPart = key.Number is double n
                ? (n + numberOffset).ToString("00000000.000", System.Globalization.CultureInfo.InvariantCulture)
                : " ";
            return $"{key.Prefix}{delimiter}{numberPart}{delimiter}{key.Suffix}";
        }
    }

    public ComicVineIssueCandidateViewModel(ComicVineIssueSummary issue, Action<ComicVineIssueCandidateViewModel> select)
    {
        Issue = issue;
        _select = select;
    }

    [RelayCommand]
    private void Select() => _select(this);
}

/// <summary>
/// Backs <see cref="ComicVineIssueReviewDialogView"/> (docs/superpowers/specs/2026-09-13-cluster-
/// scraper-ui-redesign-design.md §3) - CE's second dialog ("Choose a Comic Book Issue"), which
/// Paperbunkr never had at all until this pass (verified: <c>ScrapeOrchestrator.FindIssueDetailsAsync</c>
/// matched the issue number silently, no dialog existed anywhere in the codebase for this step).
///
/// Same list+cover-pane shape as <see cref="ComicVineMatchReviewDialogViewModel"/> for visual
/// consistency, applied to <see cref="ComicVineIssueSummary"/> rows instead of volumes - no
/// source/sort toolbar here, since issues within one already-chosen volume don't need either.
///
/// <see cref="ReadOnlyPeek"/> backs the series dialog's "Show issues" link (relevant specifically
/// when <c>ConfirmIssueMatch</c> is off): only a <see cref="CloseCommand"/> is available, and closing
/// never carries a real decision back to the orchestrator - the caller wires its own no-op resolve
/// for that case (see <c>OrganizerScraperPlugin</c>).
/// </summary>
public sealed partial class ComicVineIssueReviewDialogViewModel : ObservableObject
{
    private readonly Action<ComicVineIssueReviewResult> _resolve;
    private readonly Action? _markPermanentlySkipped;

    public string BookLabel { get; }

    public bool ReadOnlyPeek { get; }

    /// <summary>Docs/superpowers/specs/2026-09-24-comicvine-scraper-fidelity-design.md Phase 3 - hides
    /// the cover pane in this dialog too (the same setting as the series match dialog).</summary>
    public bool ShowCovers { get; }

    /// <summary>Code-behind toggles this while Ctrl is held over the Skip button (CE's own visual
    /// affordance, <c>seriesform.py</c>/<c>issueform.py</c>, verified) - the view binds the Skip
    /// button's own label to it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SkipLabel))]
    private bool _isCtrlHeldOverSkip;

    public string SkipLabel => IsCtrlHeldOverSkip ? "Skip (always)" : "Skip";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private ComicVineIssueCandidateViewModel? _selectedCandidate;

    public ObservableCollection<ComicVineIssueCandidateViewModel> Candidates { get; } = new();

    public ComicVineIssueReviewDialogViewModel(
        string bookLabel,
        IReadOnlyList<ComicVineIssueSummary> issues,
        ComicVineIssueSummary? preSelected,
        bool readOnlyPeek,
        Action<ComicVineIssueReviewResult> resolve,
        bool showCovers = true,
        Action? markPermanentlySkipped = null)
    {
        BookLabel = bookLabel;
        ReadOnlyPeek = readOnlyPeek;
        ShowCovers = showCovers;
        _resolve = resolve;
        _markPermanentlySkipped = markPermanentlySkipped;

        ComicVineIssueCandidateViewModel? toSelect = null;
        foreach (ComicVineIssueSummary issue in issues.OrderBy(i => NaturalKey(i.IssueNumber), NaturalKeyComparer.Instance))
        {
            var vm = new ComicVineIssueCandidateViewModel(issue, Select);
            Candidates.Add(vm);
            if (preSelected is not null && issue.Id == preSelected.Id)
            {
                toSelect = vm;
            }
        }

        // Falls back to the first issue when nothing auto-matched (design doc §3: confirming the
        // common "the automatic match is right" case is a single click either way).
        Select(toSelect ?? Candidates.FirstOrDefault());
    }

    private void Select(ComicVineIssueCandidateViewModel? candidate) => SelectedCandidate = candidate;

    /// <summary>CommunityToolkit's generated partial hook for <see cref="SelectedCandidate"/> - fires
    /// regardless of whether the change came from <see cref="Select"/> (the row's own command) or
    /// directly from the DataGrid's own <c>SelectedItem</c> two-way binding (docs/superpowers/specs/
    /// 2026-09-24-scraper-review-tables-and-batch-summary-design.md §1.2) when the user clicks a row.</summary>
    partial void OnSelectedCandidateChanged(ComicVineIssueCandidateViewModel? oldValue, ComicVineIssueCandidateViewModel? newValue)
    {
        foreach (ComicVineIssueCandidateViewModel c in Candidates)
        {
            c.IsSelected = ReferenceEquals(c, newValue);
        }
    }

    private bool CanConfirm() => SelectedCandidate is not null;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() => _resolve(ComicVineIssueReviewResult.Confirmed(SelectedCandidate!.Issue));

    /// <summary>Apply volume-level fields only, no per-issue fields - same resilience as an
    /// unattended run finding no number match at all.</summary>
    [RelayCommand]
    private void Skip() => _resolve(ComicVineIssueReviewResult.Skipped);

    /// <summary>Ctrl-held Skip (CE's real <c>book.skip_forever()</c>, <c>issueform.py</c>, verified) -
    /// same outcome as a plain <see cref="Skip"/> for this run, plus a durable marker so every future
    /// scrape of this book, interactive or unattended, is silently excluded without asking again.</summary>
    [RelayCommand]
    private void SkipPermanently()
    {
        _markPermanentlySkipped?.Invoke();
        _resolve(ComicVineIssueReviewResult.Skipped);
    }

    /// <summary>Re-shows the series dialog for this same book (CE parity).</summary>
    [RelayCommand]
    private void GoBack() => _resolve(ComicVineIssueReviewResult.WentBack);

    [RelayCommand]
    private void Close() => _resolve(ComicVineIssueReviewResult.WentBack);

    /// <summary>Natural-sort key: leading text (lowercased/trimmed), the first embedded number, then
    /// trailing text - so "3" &lt; "Annual 1" &lt; "Annual 2" and "4" &lt; "4a" &lt; "5", matching CE's real
    /// <c>utils.py:natural_key</c> (verified) closely enough for realistic issue numbers. A string with
    /// no embedded number at all (no digits, and not one of CE's own unicode-fraction glyphs) falls
    /// back to a plain lowercased-string key, per this step's own documented fallback for unparseable
    /// numbers.</summary>
    internal static (string Prefix, double? Number, string Suffix) NaturalKey(string? raw)
    {
        string s = (raw ?? string.Empty).Trim();

        // CE's own unicode-fraction case ("5½", "¼", verified against utils.py:96-108) - the whole
        // leading run is one number, with no meaningful prefix/suffix text to sort by.
        Match fracMatch = UnicodeFractionPattern.Match(s);
        if (fracMatch.Success)
        {
            double intPart = fracMatch.Groups["int"].Success && fracMatch.Groups["int"].Value.Length > 0
                ? double.Parse(fracMatch.Groups["int"].Value, CultureInfo.InvariantCulture)
                : 0;
            double value = intPart + UnicodeFractions[fracMatch.Groups["frac"].Value];
            return (string.Empty, fracMatch.Groups["neg"].Success ? -value : value, string.Empty);
        }

        Match numberMatch = LeadingNumberPattern.Match(s);
        if (!numberMatch.Success)
        {
            return (s.ToLowerInvariant(), null, string.Empty);
        }

        string prefix = s[..numberMatch.Index].Trim().ToLowerInvariant();
        string suffix = s[(numberMatch.Index + numberMatch.Length)..].Trim().ToLowerInvariant();
        double number = double.Parse(numberMatch.Value, CultureInfo.InvariantCulture);
        return (prefix, number, suffix);
    }

    private static readonly Regex UnicodeFractionPattern = new(@"^\s*(?<neg>-)?\s*(?<int>\d*)\s*(?<frac>[⅛⅙⅕¼⅓⅜⅖½⅗⅝⅔¾⅘⅚⅞])", RegexOptions.Compiled);
    private static readonly Regex LeadingNumberPattern = new(@"-?\d+(?:\.\d+)?", RegexOptions.Compiled);

    private static readonly Dictionary<string, double> UnicodeFractions = new()
    {
        ["⅛"] = 1.0 / 8, ["⅙"] = 1.0 / 6, ["⅕"] = 0.2, ["¼"] = 0.25, ["⅓"] = 1.0 / 3,
        ["⅜"] = 3.0 / 8, ["⅖"] = 0.4, ["½"] = 0.5, ["⅗"] = 0.6, ["⅝"] = 5.0 / 8,
        ["⅔"] = 2.0 / 3, ["¾"] = 0.75, ["⅘"] = 0.8, ["⅚"] = 5.0 / 6, ["⅞"] = 7.0 / 8,
    };

    internal sealed class NaturalKeyComparer : IComparer<(string Prefix, double? Number, string Suffix)>
    {
        public static readonly NaturalKeyComparer Instance = new();

        public int Compare((string Prefix, double? Number, string Suffix) x, (string Prefix, double? Number, string Suffix) y)
        {
            int prefix = string.CompareOrdinal(x.Prefix, y.Prefix);
            if (prefix != 0)
            {
                return prefix;
            }

            int number = Nullable.Compare(x.Number, y.Number);
            return number != 0 ? number : string.CompareOrdinal(x.Suffix, y.Suffix);
        }
    }
}
