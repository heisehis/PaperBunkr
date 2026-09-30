using SharpCompress.Archives;
using SharpCompress.Readers;

namespace Paperbunkr.ShellThumbnails;

/// <summary>
/// .cbz/.cbr/.cb7/.cbt: the encoded bytes of the entry <see cref="CoverImageRules.PickCover"/> chooses, or null
/// when the archive holds no image (Windows then shows its normal icon - decision 9).
/// </summary>
internal static class ArchiveCoverFinder
{
    public static byte[]? FindCover(Stream stream)
    {
        using var archive = ArchiveFactory.OpenArchive(stream, new ReaderOptions { LeaveStreamOpen = true });
        var entries = archive.Entries.Where(e => !e.IsDirectory && e.Key is not null).ToList();
        if (entries.Any(e => e.IsEncrypted))
        {
            return null; // decision 15: protected files get no thumbnail and never a prompt
        }

        string? coverKey = CoverImageRules.PickCover(entries.Select(e => e.Key!));
        if (coverKey is null)
        {
            return null;
        }

        try
        {
            using var entryStream = entries.First(e => e.Key == coverKey).OpenEntryStream();
            return ReadAll(entryStream);
        }
        catch (Exception ex) when (ex is not TimeoutException)
        {
            // A solid archive can't open an entry out of order - walk it forward instead, the same fallback
            // SharpCompressAccessorSession uses.
            stream.Position = 0;
            using var reader = ReaderFactory.OpenReader(stream, new ReaderOptions { LeaveStreamOpen = true });
            while (reader.MoveToNextEntry())
            {
                if (reader.Entry.Key == coverKey)
                {
                    using var entryStream = reader.OpenEntryStream();
                    return ReadAll(entryStream);
                }
            }

            return null;
        }
    }

    internal static byte[] ReadAll(Stream source)
    {
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        return buffer.ToArray();
    }
}
