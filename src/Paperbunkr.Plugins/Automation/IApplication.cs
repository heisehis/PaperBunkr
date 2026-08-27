using Paperbunkr.Data.Entities;

namespace Paperbunkr.Plugins.Automation;

/// <summary>
/// Ported from ComicRackCE's <c>ComicRack.Plugins.Automation.IApplication</c> (docs/superpowers/
/// specs/2026-08-24-plugin-api-v2-design.md §4). <c>SynchronizeDevices</c> is dropped - portable
/// device sync is already excluded from Paperbunkr entirely (CE feature inventory §15), so there's
/// nothing for it to wrap.
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
}
