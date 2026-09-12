using Paperbunkr.Data.Entities;

namespace Paperbunkr.Plugins.Automation;

/// <summary>
/// Ported from ComicRackCE's <c>ComicRack.Plugins.Automation.IApplication</c> (docs/superpowers/
/// specs/2026-08-24-plugin-api-v2-design.md §4). <c>SynchronizeDevices</c> is dropped - portable
/// device sync is already excluded from Paperbunkr entirely (CE feature inventory §15), so there's
/// nothing for it to wrap. <c>AddNewBook</c>/the icon methods/<c>GetComicFields</c> close gaps the
/// v2 port left open (docs/superpowers/specs/2026-08-30-plugin-api-automation-gaps-design.md).
/// </summary>
public interface IApplication
{
    string ProductVersion { get; }

    void Restart();

    void ScanFolders();

    IEnumerable<Issue> GetLibraryBooks();

    Issue? GetBook(int issueId);

    bool RemoveBook(Issue issue);

    bool SetCustomBookThumbnail(Issue issue, byte[] imageBytes);

    byte[]? GetComicPage(Issue issue, int page);

    byte[]? GetComicThumbnail(Issue issue);

    Task<string?> ReadInternetAsync(string url);

    /// <summary>Shows a native question dialog with the given button/option text; returns the chosen option's index.</summary>
    int AskQuestion(string question, string buttonText, string optionText);

    void ShowComicInfo(IEnumerable<Issue> books);

    /// <summary>Case-insensitive match against <c>Series.Name</c>; creates a new <c>Series</c> if
    /// nothing matches. Paperbunkr's <c>Issue</c> always needs a <c>SeriesId</c> (CE's doesn't), so
    /// this is what lets a plugin target a series that doesn't exist yet.</summary>
    int GetOrCreateSeriesId(string seriesName);

    /// <summary>
    /// Creates a new fileless <c>Issue</c> under <paramref name="seriesId"/> (CE's own
    /// <c>AddNewBook</c> creates a fileless placeholder too). <paramref name="showDialog"/> opens
    /// the real Issue Properties overlay for it, same as editing any issue; cancelling out deletes
    /// the placeholder (mirrors CE's "declining the dialog aborts the add"). Fire-and-forget from
    /// the plugin's perspective when <paramref name="showDialog"/> is true - the returned
    /// <c>Issue</c> reflects the just-created row, not whatever the user does with the dialog next.
    /// </summary>
    Issue? AddNewBook(int seriesId, bool showDialog);

    /// <summary>Null unless <paramref name="issue"/>'s publisher resolves to a real bundled brand
    /// icon (not a text/letter-chip fallback).</summary>
    byte[]? GetComicPublisherIcon(Issue issue);

    /// <summary>Same as <see cref="GetComicPublisherIcon"/>, resolving <c>Issue.Imprint</c> first
    /// and falling back to <c>Issue.Publisher</c> - mirrors CE's own imprint-icon fallback.</summary>
    byte[]? GetComicImprintIcon(Issue issue);

    byte[]? GetComicAgeRatingIcon(Issue issue);

    byte[]? GetComicFormatIcon(Issue issue);

    /// <summary>Field name -&gt; translated display label, for building a field-picker UI. Not
    /// comic-specific despite the name (matches CE's own no-argument signature).</summary>
    IDictionary<string, string> GetComicFields();
}
