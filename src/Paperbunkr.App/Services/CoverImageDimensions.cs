using System.IO;

namespace Paperbunkr.App.Services;

/// <summary>
/// Reads a JPEG's pixel dimensions straight from its header markers - no full image decode, no
/// dependency on Avalonia's render interface. Cover thumbnails are always written as JPEG
/// (<see cref="CoverThumbnailService"/> uses <c>JpegBitmapEncoderOptions</c>), so the aspect-ratio
/// backfill can sweep thousands of cached covers cheaply.
/// </summary>
public static class CoverImageDimensions
{
    /// <summary>
    /// Parses <paramref name="path"/>'s SOFn marker for image width/height. Returns false (leaving
    /// both out params 0) for a missing, non-JPEG, or truncated/corrupt file.
    /// </summary>
    public static bool TryRead(string path, out int width, out int height)
    {
        width = 0;
        height = 0;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);

            // SOI
            if (reader.ReadByte() != 0xFF || reader.ReadByte() != 0xD8)
            {
                return false;
            }

            while (stream.Position < stream.Length)
            {
                byte marker = reader.ReadByte();
                if (marker != 0xFF)
                {
                    // Skip fill bytes / resync to the next marker.
                    continue;
                }

                byte type = reader.ReadByte();
                while (type == 0xFF && stream.Position < stream.Length)
                {
                    type = reader.ReadByte();
                }

                // Standalone markers with no payload.
                if (type == 0xD9 || type == 0x01 || (type >= 0xD0 && type <= 0xD7))
                {
                    continue;
                }

                if (stream.Position + 2 > stream.Length)
                {
                    return false;
                }

                int segmentLength = (reader.ReadByte() << 8) | reader.ReadByte();
                if (segmentLength < 2)
                {
                    return false;
                }

                // SOF0..SOF15, excluding DHT(C4), DAC(CC), and RSTn - the real frame headers.
                bool isStartOfFrame = type >= 0xC0 && type <= 0xCF && type != 0xC4 && type != 0xC8 && type != 0xCC;
                if (isStartOfFrame)
                {
                    if (stream.Position + 5 > stream.Length)
                    {
                        return false;
                    }

                    reader.ReadByte(); // sample precision
                    height = (reader.ReadByte() << 8) | reader.ReadByte();
                    width = (reader.ReadByte() << 8) | reader.ReadByte();
                    return width > 0 && height > 0;
                }

                stream.Position += segmentLength - 2;
            }

            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (System.Exception)
        {
            return false;
        }
    }
}
