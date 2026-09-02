using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Hand-constructs a minimal, byte-for-byte valid PalmDB + MOBI6/PalmDOC file for testing
/// <c>MobiBookSource</c> against a real file rather than a mock - same "generate via the real code
/// path" precedent as <see cref="EpubFixture"/>/<see cref="CbzFixture"/>. Byte layout matches
/// <c>PalmDbReader</c>/<c>MobiHeaderReader</c>'s own doc comments (both independently verified
/// against real-world reference documentation, not just made to agree with each other).
/// </summary>
internal static class MobiFixture
{
    /// <summary>Builds a valid file with real EXTH title/author, a cover image record, and enough text across 2+ PalmDOC records to prove multi-record reassembly.</summary>
    public static string Create(string path, string title = "Test Novel", string author = "Ada Author", bool compressed = true, string? htmlBody = null)
    {
        string html = htmlBody ?? "<html><body>"
            + "<h1>The Beginning</h1>"
            + "<p>It was a dark and stormy night. " + string.Concat(Enumerable.Repeat("Padding to force a second PalmDOC record. ", 150)) + "</p>"
            + "<h1>The End</h1>"
            + "<p>And so it ended, quietly.</p>"
            + "</body></html>";

        byte[] textBytes = Encoding.UTF8.GetBytes(html);
        var textRecords = compressed
            ? SplitAndCompress(textBytes, 4096)
            : SplitPlain(textBytes, 4096);

        byte[] coverBytes = { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00 };

        byte[] record0 = BuildRecord0(title, author, textBytes.Length, textRecords.Count, compressed ? 2 : 1, encryptionType: 0, coverRecordIndex: 1 + textRecords.Count);

        var allRecords = new List<byte[]> { record0 };
        allRecords.AddRange(textRecords);
        allRecords.Add(coverBytes);

        File.WriteAllBytes(path, BuildPdb("test-book", allRecords));
        return path;
    }

    public static string CreateDrmProtected(string path)
    {
        byte[] record0 = BuildRecord0("DRM Book", "Someone", textLength: 0, textRecordCount: 0, compressionType: 1, encryptionType: 2, coverRecordIndex: null);
        File.WriteAllBytes(path, BuildPdb("drm-book", new List<byte[]> { record0 }));
        return path;
    }

    public static string CreateHuffmanCompressed(string path)
    {
        byte[] record0 = BuildRecord0("Huffman Book", "Someone", textLength: 0, textRecordCount: 0, compressionType: 17480, encryptionType: 0, coverRecordIndex: null);
        File.WriteAllBytes(path, BuildPdb("huff-book", new List<byte[]> { record0 }));
        return path;
    }

    private static List<byte[]> SplitPlain(byte[] textBytes, int recordSize)
    {
        var records = new List<byte[]>();
        for (int offset = 0; offset < textBytes.Length; offset += recordSize)
        {
            int length = Math.Min(recordSize, textBytes.Length - offset);
            var chunk = new byte[length];
            Array.Copy(textBytes, offset, chunk, 0, length);
            records.Add(chunk);
        }

        if (records.Count == 0)
        {
            records.Add(Array.Empty<byte>());
        }

        return records;
    }

    /// <summary>Encodes each plain-text record chunk using only the PalmDOC "0x01-0x08: copy N literal bytes" rule - a valid, decoder-correct (if not space-optimal) encoding, sufficient to exercise real decompression rather than a no-op.</summary>
    private static List<byte[]> SplitAndCompress(byte[] textBytes, int recordSize)
    {
        var plainRecords = SplitPlain(textBytes, recordSize);
        var compressed = new List<byte[]>();
        foreach (var plain in plainRecords)
        {
            using var output = new MemoryStream();
            int pos = 0;
            while (pos < plain.Length)
            {
                int chunkLength = Math.Min(8, plain.Length - pos);
                output.WriteByte((byte)chunkLength);
                output.Write(plain, pos, chunkLength);
                pos += chunkLength;
            }

            compressed.Add(output.ToArray());
        }

        return compressed;
    }

    private static byte[] BuildRecord0(string title, string author, int textLength, int textRecordCount, int compressionType, int encryptionType, int? coverRecordIndex)
    {
        byte[] titleBytes = Encoding.UTF8.GetBytes(title);
        byte[] authorBytes = Encoding.UTF8.GetBytes(author);

        const int mobiHeaderLength = 232; // Comfortably past every field this fixture writes (up to offset 132's EXTH flags at record0-absolute 128-131).
        int fullNameOffset = 16 + mobiHeaderLength; // Placed right after the (fixed-length) MOBI header, before EXTH - EXTH itself is computed to start at the same fixed offset regardless, so this doesn't collide.
        int exthStart = 16 + mobiHeaderLength;

        var exthRecords = new List<(int Type, byte[] Data)>
        {
            (100, authorBytes),
            (503, titleBytes),
        };

        if (coverRecordIndex is { } coverIndex)
        {
            // EXTH_COVEROFFSET is added to the MOBI header's FirstImageIndex - this fixture sets
            // FirstImageIndex to 0 and stores the actual record index directly in the EXTH value, so
            // MobiHeaderReader's "FirstImageIndex + coverOffset" math still resolves correctly.
            exthRecords.Add((201, BigEndianBytes((uint)coverIndex)));
        }

        int exthHeaderLength = 12 + exthRecords.Sum(r => 8 + r.Data.Length);
        // EXTH block is padded to a 4-byte boundary in real files - not required for correctness
        // here since nothing after EXTH is read by this fixture's own record 0, but kept for realism.
        int exthPadding = (4 - exthHeaderLength % 4) % 4;

        // fullNameOffset points past the fixed MOBI header AND the EXTH block, since real files put
        // the full name after EXTH when EXTH is present.
        int actualFullNameOffset = exthStart + exthHeaderLength + exthPadding;

        int totalLength = actualFullNameOffset + titleBytes.Length;
        var buffer = new byte[totalLength];

        // PalmDOC header (0-15).
        WriteUInt16BE(buffer, 0, (ushort)compressionType);
        WriteUInt32BE(buffer, 4, (uint)textLength);
        WriteUInt16BE(buffer, 8, (ushort)textRecordCount);
        WriteUInt16BE(buffer, 10, 4096);
        WriteUInt16BE(buffer, 12, (ushort)encryptionType);

        // MOBI header.
        Encoding.ASCII.GetBytes("MOBI").CopyTo(buffer, 16);
        WriteUInt32BE(buffer, 20, (uint)mobiHeaderLength);
        WriteUInt32BE(buffer, 28, 65001); // text encoding = UTF-8
        WriteUInt32BE(buffer, 84, (uint)actualFullNameOffset);
        WriteUInt32BE(buffer, 88, (uint)titleBytes.Length);
        WriteUInt32BE(buffer, 108, 0); // FirstImageIndex - see EXTH coverIndex comment above
        WriteUInt32BE(buffer, 128, 0x40); // EXTH flags - bit 6 set

        // EXTH block.
        int pos = exthStart;
        Encoding.ASCII.GetBytes("EXTH").CopyTo(buffer, pos);
        WriteUInt32BE(buffer, pos + 8, (uint)exthRecords.Count);
        pos += 12;
        foreach (var (type, data) in exthRecords)
        {
            WriteUInt32BE(buffer, pos, (uint)type);
            WriteUInt32BE(buffer, pos + 4, (uint)(8 + data.Length));
            data.CopyTo(buffer, pos + 8);
            pos += 8 + data.Length;
        }

        // Full name (PalmDOC-header-referenced title fallback - EXTH 503 above takes precedence when present, this proves the fallback path independently if needed).
        titleBytes.CopyTo(buffer, actualFullNameOffset);

        return buffer;
    }

    private static byte[] BuildPdb(string name, List<byte[]> records)
    {
        const int headerSize = 78;
        int recordTableSize = records.Count * 8;
        int dataStart = headerSize + recordTableSize;

        using var stream = new MemoryStream();
        var nameBytes = new byte[32];
        Encoding.ASCII.GetBytes(name).CopyTo(nameBytes, 0);
        stream.Write(nameBytes, 0, 32);
        WriteUInt16BEToStream(stream, 0); // attributes
        WriteUInt16BEToStream(stream, 0); // version
        WriteUInt32BEToStream(stream, 0); // creation time
        WriteUInt32BEToStream(stream, 0); // modification time
        WriteUInt32BEToStream(stream, 0); // backup time
        WriteUInt32BEToStream(stream, 0); // modification number
        WriteUInt32BEToStream(stream, 0); // app info
        WriteUInt32BEToStream(stream, 0); // sort info
        stream.Write(Encoding.ASCII.GetBytes("BOOK"), 0, 4);
        stream.Write(Encoding.ASCII.GetBytes("MOBI"), 0, 4);
        WriteUInt32BEToStream(stream, 0); // unique id seed
        WriteUInt32BEToStream(stream, 0); // next record list
        WriteUInt16BEToStream(stream, (ushort)records.Count);

        int offset = dataStart;
        foreach (var record in records)
        {
            WriteUInt32BEToStream(stream, (uint)offset);
            stream.WriteByte(0); // attributes
            stream.WriteByte(0); // unique id (3 bytes)
            stream.WriteByte(0);
            stream.WriteByte(0);
            offset += record.Length;
        }

        foreach (var record in records)
        {
            stream.Write(record, 0, record.Length);
        }

        return stream.ToArray();
    }

    private static byte[] BigEndianBytes(uint value) => new[]
    {
        (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value,
    };

    private static void WriteUInt16BE(byte[] buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)(value >> 8);
        buffer[offset + 1] = (byte)value;
    }

    private static void WriteUInt32BE(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static void WriteUInt16BEToStream(MemoryStream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static void WriteUInt32BEToStream(MemoryStream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }
}
