using System.Security.Cryptography;
using System.Text;

namespace Paperbunkr.Daemon.Clients;

/// <summary>
/// Info-hashes, needed because qBittorrent's API doesn't return the hash of a torrent it was just handed: the daemon has to know it in
/// advance to find the torrent again (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md §5). Always lower-case hex (40 chars).
/// </summary>
public static class TorrentHash
{
    /// <summary>The <c>btih</c> from a magnet URI, accepting the 40-char hex form and the 32-char base32 form.</summary>
    public static string? FromMagnet(string magnet)
    {
        if (!magnet.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        const string marker = "urn:btih:";
        int start = magnet.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        int end = magnet.IndexOfAny(new[] { '&', '#' }, start);
        var raw = (end < 0 ? magnet[start..] : magnet[start..end]).Trim();

        if (raw.Length == 40 && raw.All(Uri.IsHexDigit))
        {
            return raw.ToLowerInvariant();
        }

        if (raw.Length == 32)
        {
            var bytes = FromBase32(raw);
            return bytes is null ? null : Convert.ToHexString(bytes).ToLowerInvariant();
        }

        return null;
    }

    /// <summary>SHA-1 of the bencoded <c>info</c> dictionary of a <c>.torrent</c> file; <c>null</c> when it isn't a valid torrent.</summary>
    public static string? FromTorrentFile(byte[] data)
    {
        try
        {
            int pos = 0;
            if (data.Length == 0 || data[0] != (byte)'d')
            {
                return null;
            }

            pos++;
            while (pos < data.Length && data[pos] != (byte)'e')
            {
                var key = ReadString(data, ref pos);
                int valueStart = pos;
                SkipValue(data, ref pos);
                if (key == "info")
                {
                    return Convert.ToHexString(SHA1.HashData(data.AsSpan(valueStart, pos - valueStart))).ToLowerInvariant();
                }
            }
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or FormatException or ArgumentOutOfRangeException or OverflowException)
        {
            // a malformed torrent is simply "not a torrent"
        }

        return null;
    }

    private static string ReadString(byte[] data, ref int pos)
    {
        int colon = Array.IndexOf(data, (byte)':', pos);
        int length = int.Parse(Encoding.ASCII.GetString(data, pos, colon - pos));
        var text = Encoding.UTF8.GetString(data, colon + 1, length);
        pos = colon + 1 + length;
        return text;
    }

    private static void SkipValue(byte[] data, ref int pos)
    {
        switch ((char)data[pos])
        {
            case 'i':
                pos = Array.IndexOf(data, (byte)'e', pos) + 1;
                break;
            case 'l':
                pos++;
                while (data[pos] != (byte)'e') SkipValue(data, ref pos);
                pos++;
                break;
            case 'd':
                pos++;
                while (data[pos] != (byte)'e')
                {
                    ReadString(data, ref pos);
                    SkipValue(data, ref pos);
                }

                pos++;
                break;
            default:
                int colon = Array.IndexOf(data, (byte)':', pos);
                int length = int.Parse(Encoding.ASCII.GetString(data, pos, colon - pos));
                pos = colon + 1 + length;
                break;
        }
    }

    private static byte[]? FromBase32(string text)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>(20);
        int buffer = 0, bits = 0;
        foreach (char c in text.ToUpperInvariant())
        {
            int value = alphabet.IndexOf(c);
            if (value < 0)
            {
                return null;
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return output.Count == 20 ? output.ToArray() : null;
    }
}
