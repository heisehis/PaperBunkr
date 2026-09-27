using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Paperbunkr.App.Services;

/// <summary>One page note for the Markdown export.</summary>
public sealed record ExportNote(int PageIndex, string Text);

/// <summary>One clip for the Markdown export: its saved image, the caption (may be empty) and its page.</summary>
public sealed record ExportClip(int PageIndex, string ImagePath, string? Caption);

/// <summary>
/// Writes an issue's page notes and clips as one Markdown file with a heading per page, the clip images copied next to it (docs/superpowers/specs/2026-09-26-comic-reader-inreader-reference-design.md #7). Clip images go in
/// a sibling <see cref="ImagesFolderName"/> folder and are linked relatively, so the file and the folder can be moved together.
/// </summary>
public static class NotesMarkdownExporter
{
    public const string ImagesFolderName = "notes-images";

    /// <summary>Writes <paramref name="markdownPath"/> and the images beside it. Returns the number of clip images copied. A clip whose file is gone is listed with a note instead of an image.</summary>
    public static int Write(string markdownPath, string title, IReadOnlyList<ExportNote> notes, IReadOnlyList<ExportClip> clips)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(markdownPath)) ?? ".";
        Directory.CreateDirectory(directory);
        string imagesDirectory = Path.Combine(directory, ImagesFolderName);

        var text = new StringBuilder();
        text.Append("# ").AppendLine(title).AppendLine();

        int copied = 0;
        var pages = notes.Select(n => n.PageIndex).Concat(clips.Select(c => c.PageIndex)).Distinct().OrderBy(p => p).ToList();
        foreach (int page in pages)
        {
            text.Append("## Page ").Append(page + 1).AppendLine().AppendLine();
            foreach (var note in notes.Where(n => n.PageIndex == page))
            {
                text.AppendLine(note.Text.Trim()).AppendLine();
            }

            int clipNumber = 0;
            foreach (var clip in clips.Where(c => c.PageIndex == page))
            {
                clipNumber++;
                string caption = string.IsNullOrWhiteSpace(clip.Caption) ? $"Clip {clipNumber}" : clip.Caption.Trim();
                if (File.Exists(clip.ImagePath))
                {
                    Directory.CreateDirectory(imagesDirectory);
                    string fileName = $"page{page + 1:D3}-{clipNumber}-{Path.GetFileName(clip.ImagePath)}";
                    File.Copy(clip.ImagePath, Path.Combine(imagesDirectory, fileName), overwrite: true);
                    text.Append("![").Append(EscapeAlt(caption)).Append("](").Append(ImagesFolderName).Append('/').Append(Uri.EscapeDataString(fileName)).AppendLine(")").AppendLine();
                    copied++;
                }
                else
                {
                    text.Append("*").Append(caption).AppendLine(" (the clip's image file is missing)*").AppendLine();
                }
            }
        }

        if (pages.Count == 0)
        {
            text.AppendLine("*No notes or clips.*");
        }

        File.WriteAllText(markdownPath, text.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return copied;
    }

    private static string EscapeAlt(string caption) => caption.Replace("[", "\\[").Replace("]", "\\]");
}
