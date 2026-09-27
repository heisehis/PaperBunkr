using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Region clips and the Markdown export on the reader view model (docs/superpowers/specs/2026-09-26-comic-reader-inreader-reference-design.md #7): drag a rectangle over the page, the PNG is cut from the displayed page bitmap
/// (so it includes auto-crop and levels but not the colour sliders) and remembered in a <see cref="PageClip"/> row. Paged single-page reading only. Kept in its own file so the reader view model does not grow further.
/// </summary>
public partial class ReaderScreenViewModel
{
    private readonly List<PageClip> _clips = new();

    /// <summary>The capture overlay is on: the next drag over the page clips it.</summary>
    [ObservableProperty]
    private bool _isClipMode;

    /// <summary>Where a "Save as PNG" or "Export" asks for a file or folder; test seams (default: the real pickers).</summary>
    internal Func<string, string, string, string, Task<string?>> SaveFilePicker { get; set; } =
        (title, name, extension, label) => new FilePickerService().PickSaveFileAsync(title, name, extension, label);

    internal Func<string, Task<string?>> FolderPicker { get; set; } = title => new FilePickerService().PickFolderAsync(title);

    private void LoadClips(PaperbunkrDbContext context, int issueId)
    {
        _clips.Clear();
        _clips.AddRange(context.PageClips.Where(c => c.IssueId == issueId).OrderBy(c => c.PageNumber).ThenBy(c => c.CreatedTime));
        IsClipMode = false;
    }

    private void AddClipRows(List<NoteListItem> rows)
    {
        foreach (var clip in _clips)
        {
            rows.Add(new NoteListItem
            {
                Kind = NoteItemKind.Clip,
                PageIndex = clip.PageNumber,
                Text = clip.Caption,
                ClipId = clip.Id,
                ImagePath = clip.ImagePath,
                Thumbnail = TryLoadClipThumbnail(clip.ImagePath),
            });
        }
    }

    private static Bitmap? TryLoadClipThumbnail(string path)
    {
        try
        {
            return File.Exists(path) ? new Bitmap(path) : null;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Palette, page menu, drawer button and key: turn the capture overlay on or off.</summary>
    [RelayCommand]
    private void ToggleClipMode()
    {
        if (IsClipMode)
        {
            IsClipMode = false;
            return;
        }

        if (IsContinuousMode)
        {
            ToastRequested?.Invoke(new ToastRequest("Clips are cut from a single page", "Switch to a paged reading mode to clip a region."));
            return;
        }

        if (CurrentPageSecondary is not null)
        {
            ToastRequested?.Invoke(new ToastRequest("Clips need single-page layout", "Turn double-page mode off first."));
            return;
        }

        IsClipMode = true;
        ToastRequested?.Invoke(new ToastRequest("Clip a region", "Drag a rectangle over the page. Escape cancels."));
    }

    /// <summary>The capture overlay finished a drag: <paramref name="fractions"/> is the rectangle as fractions (0-1) of the displayed page.</summary>
    public void CaptureClip(Rect fractions)
    {
        IsClipMode = false;
        if (_loadedIssueId is not int issueId || CurrentPage is not { } page || fractions.Width <= 0 || fractions.Height <= 0)
        {
            return;
        }

        try
        {
            string directory = AppDataPaths.Combine("annotations", "clips", issueId.ToString());
            string imagePath = BookAnnotationCaptureService.CropAndSave(page, fractions.X, fractions.Y, fractions.Width, fractions.Height, directory);
            var clip = new PageClip
            {
                IssueId = issueId,
                PageNumber = _currentPageIndex,
                RectX = fractions.X,
                RectY = fractions.Y,
                RectWidth = fractions.Width,
                RectHeight = fractions.Height,
                ImagePath = imagePath,
                CreatedTime = DateTime.UtcNow,
            };
            using (var context = PaperbunkrDb.CreateContext(includeRemote: true))
            {
                context.PageClips.Add(clip);
                context.SaveChanges();
            }

            _clips.Add(clip);
            RebuildNoteItems();
            IsDrawerOpen = true;
            IsNotesSectionExpanded = true;
            ToastRequested?.Invoke(new ToastRequest("Clip saved", $"Page {_currentPageIndex + 1}", ToastSeverity.Success));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException or InvalidOperationException)
        {
            ToastRequested?.Invoke(new ToastRequest("Couldn't save the clip", ex.Message, ToastSeverity.Error));
        }
    }

    [RelayCommand]
    private async Task CopyClipAsync(NoteListItem? item)
    {
        if (item is not { IsClip: true, ImagePath: { } path } || !File.Exists(path))
        {
            return;
        }

        var bitmap = new Bitmap(path);
        bool copied = await BitmapClipboardWriter(bitmap);
        if (!copied)
        {
            bitmap.Dispose();
        }

        ToastRequested?.Invoke(copied
            ? new ToastRequest("Clip copied", item.PageLabel, ToastSeverity.Success)
            : new ToastRequest("Couldn't copy", "The clipboard is not available.", ToastSeverity.Error));
    }

    [RelayCommand]
    private async Task SaveClipAsync(NoteListItem? item)
    {
        if (item is not { IsClip: true, ImagePath: { } path } || !File.Exists(path))
        {
            return;
        }

        string? target = await SaveFilePicker("Save clip as", $"{ExportBaseName(item.PageLabel)} clip", "png", "PNG Image");
        if (target is null)
        {
            return;
        }

        try
        {
            File.Copy(path, target, overwrite: true);
            ToastRequested?.Invoke(new ToastRequest("Clip saved", Path.GetFileName(target), ToastSeverity.Success));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ToastRequested?.Invoke(new ToastRequest("Couldn't save the clip", ex.Message, ToastSeverity.Error));
        }
    }

    [RelayCommand]
    private void DeleteClip(NoteListItem? item)
    {
        if (item is not { IsClip: true })
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext(includeRemote: true))
        {
            var row = context.PageClips.FirstOrDefault(c => c.Id == item.ClipId);
            if (row is not null)
            {
                context.PageClips.Remove(row);
                context.SaveChanges();
            }
        }

        try
        {
            if (!string.IsNullOrEmpty(item.ImagePath) && File.Exists(item.ImagePath))
            {
                File.Delete(item.ImagePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The row is gone; a file that cannot be deleted right now is left for the next clean-up.
        }

        // Deferred a dispatcher tick: this runs from a button inside the very list row that is about to be removed (see CLAUDE.md, "don't remove/detach a control from inside a routed event it's still raising").
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _clips.RemoveAll(c => c.Id == item.ClipId);
            RebuildNoteItems();
        });
    }

    /// <summary>The caption box of a clip lost focus with <paramref name="caption"/>: stores it.</summary>
    internal void CommitClipCaption(NoteListItem item, string? caption)
    {
        if (!item.IsClip)
        {
            return;
        }

        string? clean = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
        if (clean is { Length: > PageClip.MaxCaptionLength })
        {
            clean = clean[..PageClip.MaxCaptionLength];
        }

        using var context = PaperbunkrDb.CreateContext(includeRemote: true);
        var row = context.PageClips.FirstOrDefault(c => c.Id == item.ClipId);
        if (row is null || row.Caption == clean)
        {
            return;
        }

        row.Caption = clean;
        context.SaveChanges();
        var cached = _clips.FirstOrDefault(c => c.Id == item.ClipId);
        if (cached is not null)
        {
            cached.Caption = clean;
        }
    }

    /// <summary>Palette and the NOTES section: writes this issue's notes and clips as one Markdown file with an images folder beside it.</summary>
    [RelayCommand]
    private async Task ExportNotesAsync()
    {
        if (_pageNotes.Count == 0 && _clips.Count == 0)
        {
            ToastRequested?.Invoke(new ToastRequest("Nothing to export", "This issue has no notes or clips yet."));
            return;
        }

        string title = $"{Info.Model?.SeriesLine ?? LoadedIssue?.Series?.Name ?? "Comic"} - notes";
        string? path = await SaveFilePicker("Export notes and clips", title, "md", "Markdown");
        if (path is null)
        {
            return;
        }

        try
        {
            int images = NotesMarkdownExporter.Write(
                path,
                title,
                _pageNotes.Values.Select(n => new ExportNote(n.PageNumber, n.Text)).ToList(),
                _clips.Select(c => new ExportClip(c.PageNumber, c.ImagePath, c.Caption)).ToList());
            ToastRequested?.Invoke(new ToastRequest("Notes exported", images > 0 ? $"{Path.GetFileName(path)} and {images} image(s)" : Path.GetFileName(path), ToastSeverity.Success));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ToastRequested?.Invoke(new ToastRequest("Couldn't export", ex.Message, ToastSeverity.Error));
        }
    }
}
