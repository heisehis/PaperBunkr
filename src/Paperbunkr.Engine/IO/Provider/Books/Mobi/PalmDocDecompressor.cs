using System;
using System.IO;

namespace cYo.Projects.ComicRack.Engine.IO.Provider.Books.Mobi
{
	/// <summary>
	/// Decompresses a single PalmDOC-compressed text record (docs/superpowers/specs/2026-09-01-books-
	/// format-ingestion-fb2-mobi-design.md - MOBI/AZW3 foundation layer). Each MOBI text record is
	/// compressed independently (no cross-record back-references), so this operates one record at a
	/// time - <see cref="MobiBookSource"/> concatenates the results.
	///
	/// Byte-range rules verified directly against Calibre's own format documentation
	/// (format_docs/compression/palmdoc.txt) rather than recalled from memory:
	/// - 0x00 and 0x09-0x7F: copy the byte unmodified (0x00 is not "reserved", just a literal ASCII
	///   NUL, which text records legitimately don't contain but the algorithm doesn't special-case).
	/// - 0x01-0x08: the next N bytes (N = this byte's value) are copied unmodified.
	/// - 0x80-0xBF: a 2-byte back-reference. Discard the top 2 bits of this byte ('10'), combine the
	///   remaining 6 bits with the next byte's 8 bits into a 14-bit value; the top 11 bits are a
	///   backward distance from the current output position, the bottom 3 bits are a length code
	///   (copy length = code + 3, so 3-10 bytes). Distance can be less than length (self-overlapping
	///   copy is valid and expected, standard LZ77-style).
	/// - 0xC0-0xFF: expands to 2 characters - a space, then this byte XORed with 0x80.
	/// </summary>
	internal static class PalmDocDecompressor
	{
		public static byte[] Decompress(byte[] compressed)
		{
			using var output = new MemoryStream(compressed.Length * 2);
			int i = 0;
			while (i < compressed.Length)
			{
				byte b = compressed[i];

				if (b == 0x00 || (b >= 0x09 && b <= 0x7F))
				{
					output.WriteByte(b);
					i++;
				}
				else if (b >= 0x01 && b <= 0x08)
				{
					int count = b;
					i++;
					for (int n = 0; n < count && i < compressed.Length; n++, i++)
					{
						output.WriteByte(compressed[i]);
					}
				}
				else if (b >= 0x80 && b <= 0xBF)
				{
					if (i + 1 >= compressed.Length)
					{
						throw new InvalidDataException("PalmDOC-compressed record ends mid back-reference.");
					}

					int combined = ((b & 0x3F) << 8) | compressed[i + 1];
					int distance = combined >> 3;
					int length = (combined & 0x07) + 3;
					i += 2;

					long copyStart = output.Length - distance;
					if (copyStart < 0)
					{
						throw new InvalidDataException($"PalmDOC back-reference distance {distance} exceeds decompressed-so-far length {output.Length}.");
					}

					// Re-fetch the buffer reference on every iteration, not once before the loop - a
					// self-overlapping copy (distance < length) must see bytes this same loop just
					// wrote, and WriteByte can reallocate MemoryStream's internal array mid-loop once
					// capacity is exceeded, which would silently strand a cached reference on the old,
					// now-stale array.
					for (int n = 0; n < length; n++)
					{
						output.WriteByte(output.GetBuffer()[copyStart + n]);
					}
				}
				else // 0xC0-0xFF
				{
					output.WriteByte((byte)' ');
					output.WriteByte((byte)(b ^ 0x80));
					i++;
				}
			}

			return output.ToArray();
		}
	}
}
