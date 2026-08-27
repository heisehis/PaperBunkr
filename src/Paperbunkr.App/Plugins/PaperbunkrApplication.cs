using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.Views;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins.Automation;

namespace Paperbunkr.App.Plugins;

/// <summary>
/// Real adapter for <see cref="IApplication"/> (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md
/// §4), wrapping the same services the rest of the app already uses. <c>SynchronizeDevices</c> is
/// dropped from the interface entirely - portable device sync doesn't exist in Paperbunkr.
/// </summary>
public sealed class PaperbunkrApplication : IApplication
{
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

    public IEnumerable<Issue> GetLibraryBooks()
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.Issues.Include(i => i.Series).ToList();
    }

    public Issue? GetBook(int issueId)
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.Issues.Include(i => i.Series).FirstOrDefault(i => i.Id == issueId);
    }

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
        string path = CoverThumbnailPaths.GetCachePath(issue.Id);
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

    public int AskQuestion(string question, string buttonText, string optionText) =>
        PluginQuestionDialog.ShowModal(question, buttonText, optionText);

    public void ShowComicInfo(IEnumerable<Issue> books)
    {
        // No standalone "comic info" popover exists yet outside the Detail screen (docs/superpowers/
        // specs/2026-08-24-plugin-api-v2-design.md §5's ComicInfoUI hook covers that surface instead) -
        // left as a documented no-op rather than a fake window.
    }
}
