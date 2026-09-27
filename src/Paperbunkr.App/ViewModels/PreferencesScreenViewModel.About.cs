using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Preferences → About (docs/superpowers/specs/2026-09-26-about-polish-design.md): the Overview / Changelog / Legal &amp; notices tabs,
/// the changelog rows, the legal document viewer, and the copy / project-link / folder buttons. The update check itself stays in
/// the main file next to the rest of the update plumbing.
/// </summary>
public partial class PreferencesScreenViewModel
{
    /// <summary>Tab keys of the About section; the same strings are <see cref="PreferenceIndexEntry.SubTab"/> values and the panels' keys.</summary>
    public static class AboutTabKeys
    {
        public const string Overview = "overview";
        public const string Changelog = "changelog";
        public const string Legal = "legal";
    }

    private static readonly Regex LastUpdatedLine = new(@"^\s*\*\*Last updated:\*\*\s*(?<date>.+?)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

    /// <summary>The About section's tabs. In memory only: reopening Preferences after a restart starts on Overview.</summary>
    public SettingsTabs AboutTabs { get; } = new(new SettingsTabItem[]
    {
        new(AboutTabKeys.Overview, "Overview"),
        new(AboutTabKeys.Changelog, "Changelog"),
        new(AboutTabKeys.Legal, "Legal & notices"),
    });

    /// <summary>The legal/notice documents, in display order, for the Legal &amp; notices rows.</summary>
    public IReadOnlyList<LegalDocument> LegalDocumentList => LegalDocuments.All;

    /// <summary>"Version 0.7.3-beta · build 9cc0b62 · Comic and manga library" under the header title.</summary>
    public string AboutVersionLine => BuildLabel is { Length: > 0 } build
        ? $"Version {CurrentVersion} · build {build} · Comic and manga library"
        : $"Version {CurrentVersion} · Comic and manga library";

    /// <summary>How long a Copy button reads "Copied" after a successful copy. Internal so tests needn't wait.</summary>
    internal TimeSpan CopiedFeedbackDuration { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Clipboard seam, as in the reader's copy commands; tests swap it for a recorder.</summary>
    internal Func<string, Task> ClipboardTextWriter { get; set; } = ClipboardHelper.CopyTextAsync;

    /// <summary>Shell-open seam for URLs and folders; tests swap it for a recorder.</summary>
    internal Func<string, bool> ShellOpener { get; set; } = ExternalLinks.TryShellOpen;

    // ===================== Changelog =====================

    /// <summary>Visible changelog entries, newest first; the current one marked and expanded (Q14).</summary>
    public ObservableCollection<ChangelogRow> ChangelogRows { get; } = new();

    [ObservableProperty]
    private bool _hasChangelog;

    /// <summary>
    /// Parses the bundled CHANGELOG.md (copied next to the exe by the csproj). A missing file (a dev build run before the copy step)
    /// leaves the list empty and the tab says so, rather than throwing.
    /// </summary>
    private void RefreshChangelog()
    {
        ChangelogRows.Clear();
        foreach (var row in ChangelogSelection.BuildRows(ChangelogParser.LoadBundledEntries(), ReleaseVersion.Current))
        {
            ChangelogRows.Add(row);
        }

        HasChangelog = ChangelogRows.Count > 0;
    }

    // ===================== Legal viewer =====================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedLegalDocumentTitle))]
    private LegalDocument? _selectedLegalDocument;

    public string? SelectedLegalDocumentTitle => SelectedLegalDocument?.Title;

    [ObservableProperty]
    private IReadOnlyList<MdBlock> _selectedLegalDocumentBlocks = Array.Empty<MdBlock>();

    /// <summary>The document's own "Last updated" date, lifted out of its body into the viewer header. Null if it has none.</summary>
    [ObservableProperty]
    private string? _selectedLegalDocumentUpdated;

    [ObservableProperty]
    private bool _isLegalDocumentViewerOpen;

    [ObservableProperty]
    private bool _legalDocumentCopied;

    [ObservableProperty]
    private bool _versionInfoCopied;

    private string _selectedLegalDocumentText = string.Empty;

    /// <summary>
    /// Opens one of the <see cref="LegalDocuments"/> bundled next to the exe in the in-app viewer. Also how a link from one document to
    /// another swaps the viewer's content. Unknown or missing files do nothing - a dev build run before the csproj copy step
    /// shouldn't crash.
    /// </summary>
    [RelayCommand]
    private void OpenLegalDocument(string? fileName)
    {
        var document = LegalDocuments.Find(fileName);
        if (document is null)
        {
            return;
        }

        string path = Path.Combine(AppContext.BaseDirectory, document.FileName);
        if (!File.Exists(path))
        {
            return;
        }

        string text = File.ReadAllText(path);
        string? updated = null;
        string body = text;
        if (!document.Preformatted)
        {
            var match = LastUpdatedLine.Match(text);
            if (match.Success)
            {
                updated = match.Groups["date"].Value;
                body = text.Remove(match.Index, match.Length);
            }
        }

        _selectedLegalDocumentText = text;
        SelectedLegalDocument = document;
        SelectedLegalDocumentUpdated = updated;
        SelectedLegalDocumentBlocks = document.Preformatted ? MarkdownLite.Preformatted(text) : MarkdownLite.Parse(body);
        LegalDocumentCopied = false;
        IsLegalDocumentViewerOpen = true;
    }

    [RelayCommand]
    private void CloseLegalDocumentViewer() => IsLegalDocumentViewerOpen = false;

    /// <summary>A link clicked inside a legal document: another bundled document opens in place, a web link in the browser.</summary>
    [RelayCommand]
    private void OpenLegalLink(string? target)
    {
        switch (LinkTargetResolver.Resolve(target))
        {
            case LinkTargetKind.Document:
                OpenLegalDocument(target);
                break;
            case LinkTargetKind.Web:
                ShellOpener(target!.Trim());
                break;
        }
    }

    [RelayCommand]
    private async Task CopyLegalDocument()
    {
        if (_selectedLegalDocumentText.Length == 0 || !await TryCopy(_selectedLegalDocumentText))
        {
            return;
        }

        LegalDocumentCopied = true;
        await Task.Delay(CopiedFeedbackDuration);
        LegalDocumentCopied = false;
    }

    // ===================== Header + Overview =====================

    [RelayCommand]
    private async Task CopyVersionInfo()
    {
        if (!await TryCopy(AboutInfo.VersionReport()))
        {
            return;
        }

        VersionInfoCopied = true;
        await Task.Delay(CopiedFeedbackDuration);
        VersionInfoCopied = false;
    }

    /// <summary>Opens one of the <see cref="ProjectLinks"/> (the only URLs the rows pass) in the browser.</summary>
    [RelayCommand]
    private void OpenProjectLink(string? url)
    {
        if (LinkTargetResolver.Resolve(url) == LinkTargetKind.Web)
        {
            ShellOpener(url!);
        }
    }

    /// <summary>Opens <c>%AppData%\Paperbunkr\logs</c> (startup.log and crash logs) - handy when filing a bug.</summary>
    [RelayCommand]
    private void OpenLogsFolder() => OpenAppFolder(AppDataPaths.Combine("logs"));

    /// <summary>Opens <c>%AppData%\Paperbunkr</c> (database, covers, backups, plugins).</summary>
    [RelayCommand]
    private void OpenDataFolder() => OpenAppFolder(AppDataPaths.Root);

    private void OpenAppFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception)
        {
            return;
        }

        ShellOpener(path);
    }

    private async Task<bool> TryCopy(string text)
    {
        try
        {
            await ClipboardTextWriter(text);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
