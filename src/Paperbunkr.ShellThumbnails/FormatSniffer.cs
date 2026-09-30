using System.IO.Compression;
using System.Text;

namespace Paperbunkr.ShellThumbnails;

/// <summary>
/// Picks which cover finder to use. The name Windows reports for the stream is normally the file name, so its
/// extension decides; when there's no usable name, the first bytes do (a misnamed archive is fine either
/// way - SharpCompress detects the real archive format itself).
/// </summary>
internal static class FormatSniffer
{
    private static readonly byte[] SevenZipSignature = { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C };

    public static string? ExtensionFor(string? streamName, Stream stream)
    {
        string? fromName = streamName is null ? null : Path.GetExtension(streamName).ToLowerInvariant();
        if (fromName is not null && ThumbnailRenderer.Extensions.Contains(fromName))
        {
            return fromName;
        }

        return Sniff(stream);
    }

    internal static string? Sniff(Stream stream)
    {
        byte[] head = new byte[300];
        stream.Position = 0;
        int n = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        stream.Position = 0;
        var span = head.AsSpan(0, n);

        if (span.StartsWith("%PDF"u8))
        {
            return ".pdf";
        }

        if (span.StartsWith("AT&TFORM"u8))
        {
            return ".djvu";
        }

        if (span.StartsWith("Rar!"u8))
        {
            return ".cbr";
        }

        if (span.StartsWith(SevenZipSignature))
        {
            return ".cb7";
        }

        if (n >= 68 && Encoding.ASCII.GetString(head, 60, 8) == "BOOKMOBI")
        {
            return ".mobi";
        }

        if (n >= 262 && Encoding.ASCII.GetString(head, 257, 5) == "ustar")
        {
            return ".cbt";
        }

        if (span.StartsWith("PK\u0003\u0004"u8))
        {
            return IsEpub(stream) ? ".epub" : ".cbz";
        }

        return null;
    }

    private static bool IsEpub(Stream stream)
    {
        try
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var mimetype = zip.GetEntry("mimetype");
            if (mimetype is null)
            {
                return false;
            }

            using var reader = new StreamReader(mimetype.Open());
            return reader.ReadToEnd().Trim() == "application/epub+zip";
        }
        catch (InvalidDataException)
        {
            return false;
        }
        finally
        {
            stream.Position = 0;
        }
    }
}
