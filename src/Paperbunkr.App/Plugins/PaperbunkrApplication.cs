using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins.Automation;

namespace Paperbunkr.App.Plugins;

/// <summary>
/// Real adapter for <see cref="IApplication"/> (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md
/// §4; gaps closed in docs/superpowers/specs/2026-08-30-plugin-api-automation-gaps-design.md),
/// wrapping the same services the rest of the app already uses. <c>SynchronizeDevices</c> is
/// dropped from the interface entirely - portable device sync doesn't exist in Paperbunkr.
/// </summary>
public sealed class PaperbunkrApplication : IApplication
{
    /// <summary>Rasterized icon size in px for the icon methods - not tied to any on-screen display
    /// size (there isn't one here), just a reasonably crisp fixed size for a plugin consumer.</summary>
    private const int IconSize = 64;

    private readonly MainViewModel _main;

    public PaperbunkrApplication(MainViewModel main) => _main = main;

    public string ProductVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

    public void Restart()
    {
        DiagnosticsService.LogMilestone("Restart requested by a plugin command.");
        // Full process-relaunch orchestration lives in the crash-reporter's Restart path
        // (Views/CrashReportWindow.axaml.cs callers) - a plugin-triggered restart is a rare enough
        // path that it isn't worth duplicating that logic here in this pass.
    }

    public void ScanFolders()
    {
        _ = new LibraryFolderScanner().ScanAllAsync(new Progress<(int Done, int Total)>());
    }

    /// <summary>
    /// Eager-loads Tags/CustomValues/MetadataProposals/Bookmarks alongside Series (docs/superpowers/
    /// specs/2026-08-28-plugin-api-v3-data-manager-design.md §3). No lazy-loading proxies are
    /// configured anywhere, so without these Includes a plugin would silently see empty collections
    /// - the worse failure mode. The result set now matches what <c>SmartListQueryBuilder</c> sees,
    /// which is what makes <see cref="Paperbunkr.Plugins.Automation.IRulesEngine"/> trustworthy:
    /// evaluating a rule against this output and asking <c>IRulesEngine</c> to evaluate the same
    /// rule see the same underlying data.
    /// </summary>
    public IEnumerable<Issue> GetLibraryBooks()
    {
        using var context = PaperbunkrDb.CreateContext();
        return IncludePluginRelations(context.Issues).ToList();
    }

    /// <summary>Same eager-load set as <see cref="GetLibraryBooks"/> (docs §3).</summary>
    public Issue? GetBook(int issueId)
    {
        using var context = PaperbunkrDb.CreateContext();
        return IncludePluginRelations(context.Issues).FirstOrDefault(i => i.Id == issueId);
    }

    private static IQueryable<Issue> IncludePluginRelations(IQueryable<Issue> issues) => issues
        .Include(i => i.Series)
        .Include(i => i.Tags)
        .Include(i => i.CustomValues)
        .Include(i => i.MetadataProposals)
        .Include(i => i.Bookmarks);

    public bool RemoveBook(Issue issue)
    {
        using var context = PaperbunkrDb.CreateContext();
        var tracked = context.Issues.Find(issue.Id);
        if (tracked is null)
        {
            return false;
        }

        LibraryDeletionHelper.RemoveIssue(context, tracked);
        context.SaveChanges();
        return true;
    }

    public bool SetCustomBookThumbnail(Issue issue, byte[] imageBytes)
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"paperbunkr-plugin-cover-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(tempPath, imageBytes);
            return new CoverThumbnailService().TrySetCustomCover(issue.Id, tempPath);
        }
        finally
        {
            try { File.Delete(tempPath); } catch (IOException) { }
        }
    }

    public byte[]? GetComicPage(Issue issue, int page)
    {
        if (issue.FilePath is null)
        {
            return null;
        }

        using var decoder = PageImageDecoder.TryOpen(issue.FilePath);
        if (decoder is null || page < 0 || page >= decoder.PageCount)
        {
            return null;
        }

        Bitmap bitmap = decoder.GetPage(page);
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        return stream.ToArray();
    }

    public byte[]? GetComicThumbnail(Issue issue)
    {
        string stem = CoverFingerprint.Stem(issue.Id, issue.FilePath, issue.FileSize);
        string path = CoverThumbnailPaths.GetCachePath(stem);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    public async Task<string?> ReadInternetAsync(string url)
    {
        using var client = new HttpClient();
        try
        {
            return await client.GetStringAsync(url).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// Shows the native question dialog and returns the chosen option index (0 = primary button,
    /// 1 = secondary). Answering affirmatively (index 0) also opens the per-invocation write
    /// confirmation gate for a <c>confirmWrites="true"</c> command (docs/superpowers/specs/2026-08-28-
    /// plugin-api-v3-data-manager-design.md §5) - the one native primitive that gate reuses.
    /// </summary>
    public int AskQuestion(string question, string buttonText, string optionText)
    {
        int answer = PluginQuestionDialog.ShowModal(question, buttonText, optionText);
        if (answer == 0 && Paperbunkr.Plugins.PluginInvocationContext.Current is { RequiresWriteConfirmation: true } ctx)
        {
            ctx.WritesConfirmed = true;
        }

        return answer;
    }

    public void ShowComicInfo(IEnumerable<Issue> books)
    {
        // No standalone "comic info" popover exists yet outside the Detail screen (docs/superpowers/
        // specs/2026-08-24-plugin-api-v2-design.md §5's ComicInfoUI hook covers that surface instead) -
        // left as a documented no-op rather than a fake window.
    }

    public int GetOrCreateSeriesId(string seriesName)
    {
        using var context = PaperbunkrDb.CreateContext();
        string normalized = seriesName.ToLowerInvariant();
        var existing = context.Series.FirstOrDefault(s => s.Name.ToLower() == normalized);
        if (existing is not null)
        {
            return existing.Id;
        }

        var series = new Series { Name = seriesName };
        context.Series.Add(series);
        context.SaveChanges();
        return series.Id;
    }

    /// <summary>
    /// Creates a fileless Issue under <paramref name="seriesId"/> - CE's own <c>AddNewBook</c>
    /// creates a fileless placeholder too. <paramref name="showDialog"/> reuses the exact flow the
    /// Library screen's own "Add Issue" panel already drives
    /// (<see cref="MainViewModel.OpenIssuePropertiesForPlugin"/>, deleteIfUnedited: true) so
    /// cancelling the overlay deletes this placeholder - no new delete-on-cancel logic needed.
    /// </summary>
    public Issue? AddNewBook(int seriesId, bool showDialog)
    {
        using var context = PaperbunkrDb.CreateContext();
        var issue = new Issue { SeriesId = seriesId, AddedTime = DateTime.UtcNow };
        context.Issues.Add(issue);
        context.SaveChanges();

        if (showDialog)
        {
            _main.OpenIssuePropertiesForPlugin(issue.Id, seriesId, deleteIfUnedited: true);
        }

        return GetBook(issue.Id);
    }

    public byte[]? GetComicPublisherIcon(Issue issue) => RasterizeMark(MarkResolver.Instance.ResolvePublisher(issue.Publisher));

    /// <summary>Mirrors CE's own imprint-icon fallback: resolve Imprint first, fall back to Publisher.</summary>
    public byte[]? GetComicImprintIcon(Issue issue)
    {
        var imprintMark = MarkResolver.Instance.ResolvePublisher(issue.Imprint);
        return imprintMark.Kind is MarkKind.SvgAsset or MarkKind.Flag
            ? RasterizeMark(imprintMark)
            : RasterizeMark(MarkResolver.Instance.ResolvePublisher(issue.Publisher));
    }

    public byte[]? GetComicAgeRatingIcon(Issue issue) => RasterizeMark(MarkResolver.Instance.ResolveAgeRating(issue.AgeRating));

    public byte[]? GetComicFormatIcon(Issue issue) => RasterizeMark(MarkResolver.Instance.ResolveFormat(issue.Format));

    /// <summary>Only <see cref="MarkKind.SvgAsset"/>/<see cref="MarkKind.Flag"/> have a bundled
    /// image to rasterize - <see cref="MarkKind.LetterMark"/>/<see cref="MarkKind.Glyph"/>/
    /// <see cref="MarkKind.Text"/>/<see cref="MarkKind.None"/> return null, same as CE returning
    /// nothing when it has no real brand icon for a value.</summary>
    private static byte[]? RasterizeMark(MarkSpec spec)
    {
        if (spec.Kind is not (MarkKind.SvgAsset or MarkKind.Flag) || spec.AssetPath is not { } path)
        {
            return null;
        }

        // Render() memoises for the process lifetime (SvgMarkRenderer's own doc comment) - the
        // returned Bitmap is shared/cached, not owned by this call, so it must not be disposed
        // here (doing so previously poisoned the cache for every later lookup of the same key).
        Bitmap? rendered = SvgMarkRenderer.Render(path, IconSize, tint: null);
        if (rendered is null)
        {
            return null;
        }

        using var stream = new MemoryStream();
        rendered.Save(stream, new PngBitmapEncoderOptions());
        return stream.ToArray();
    }

    public IDictionary<string, string> GetComicFields() =>
        IssueListFieldCatalog.SortFields.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value.DisplayName);
}
