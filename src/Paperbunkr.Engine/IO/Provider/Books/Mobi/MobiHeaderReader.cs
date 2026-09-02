using System;
using System.IO;
using System.Text;

namespace cYo.Projects.ComicRack.Engine.IO.Provider.Books.Mobi
{
	/// <summary>
	/// Parses record 0 of a MOBI/AZW3 file (docs/superpowers/specs/2026-09-01-books-format-ingestion-
	/// fb2-mobi-design.md - MOBI/AZW3 foundation layer): the 16-byte PalmDOC header, the MOBI header,
	/// and (if present) the EXTH metadata block. Byte offsets verified directly against the MobileRead
	/// Wiki's MOBI article and the 766b/mobi Go reference implementation, not recalled from memory -
	/// this format has enough near-miss offsets in circulation online that guessing wasn't worth the
	/// risk of a silently-wrong parse.
	/// </summary>
	internal sealed class MobiHeaderReader
	{
		// PalmDOC header (bytes 0-15 of record 0).
		private const int CompressionTypeOffset = 0;
		private const int TextLengthOffset = 4;
		private const int TextRecordCountOffset = 8;
		private const int TextRecordSizeOffset = 10;
		private const int EncryptionTypeOffset = 12;

		// MOBI header (starts at byte 16 of record 0) - these offsets are already record-0-absolute,
		// not relative to the MOBI header's own start.
		private const int MobiHeaderStart = 16;
		private const int MobiIdentifierOffset = 16;
		private const int MobiHeaderLengthOffset = 20;
		private const int TextEncodingOffset = 28;
		private const int FullNameOffsetOffset = 84;
		private const int FullNameLengthOffset = 88;
		private const int FirstImageIndexOffset = 108;
		private const int ExthFlagsOffset = 128;
		private const uint ExthPresentFlag = 0x40;

		public int CompressionType { get; }

		public int EncryptionType { get; }

		public int TextRecordCount { get; }

		public int TextRecordSize { get; }

		/// <summary>Windows code page from the MOBI header's text-encoding field (65001=UTF-8, 1252=CP1252/WinLatin1) - defaults to UTF-8 (65001) when absent or unrecognized.</summary>
		public int TextEncodingCodePage { get; } = 65001;

		public string? Title { get; }

		public string? Author { get; }

		/// <summary>PDB record index holding the cover image (EXTH cover-offset tag added to the MOBI header's first-image-record index), or null if neither is present.</summary>
		public int? CoverRecordIndex { get; }

		/// <summary>EXTH tag 121 ("KF8 Boundary") - the PDB record index where the KF8 part of a combined MOBI6+KF8 file begins, or null for a MOBI6-only file. Consumed by the (separate, time-boxed) KF8 skeleton reconstruction spike.</summary>
		public int? Kf8BoundaryRecordIndex { get; }

		public MobiHeaderReader(byte[] record0)
		{
			if (record0.Length < 16)
			{
				throw new InvalidDataException("MOBI record 0 is too short to contain a PalmDOC header.");
			}

			CompressionType = PalmDbReader.ReadUInt16BE(record0, CompressionTypeOffset);
			TextRecordCount = PalmDbReader.ReadUInt16BE(record0, TextRecordCountOffset);
			TextRecordSize = PalmDbReader.ReadUInt16BE(record0, TextRecordSizeOffset);
			EncryptionType = PalmDbReader.ReadUInt16BE(record0, EncryptionTypeOffset);

			string? title = null;
			string? author = null;
			int? coverIndex = null;
			int? kf8Boundary = null;

			bool hasMobiHeader = record0.Length >= MobiIdentifierOffset + 4
				&& Encoding.ASCII.GetString(record0, MobiIdentifierOffset, 4) == "MOBI";

			if (hasMobiHeader)
			{
				int headerLength = (int)PalmDbReader.ReadUInt32BE(record0, MobiHeaderLengthOffset);

				if (record0.Length >= TextEncodingOffset + 4)
				{
					int codePage = (int)PalmDbReader.ReadUInt32BE(record0, TextEncodingOffset);
					if (codePage == 65001 || codePage == 1252)
					{
						TextEncodingCodePage = codePage;
					}
				}

				if (record0.Length >= FullNameLengthOffset + 4)
				{
					int fullNameOffset = (int)PalmDbReader.ReadUInt32BE(record0, FullNameOffsetOffset);
					int fullNameLength = (int)PalmDbReader.ReadUInt32BE(record0, FullNameLengthOffset);
					if (fullNameOffset > 0 && fullNameLength > 0 && fullNameOffset + fullNameLength <= record0.Length)
					{
						title = Encoding.UTF8.GetString(record0, fullNameOffset, fullNameLength);
					}
				}

				int firstImageIndex = -1;
				if (record0.Length >= FirstImageIndexOffset + 4)
				{
					firstImageIndex = (int)PalmDbReader.ReadUInt32BE(record0, FirstImageIndexOffset);
				}

				bool hasExth = record0.Length >= ExthFlagsOffset + 4
					&& (PalmDbReader.ReadUInt32BE(record0, ExthFlagsOffset) & ExthPresentFlag) != 0;

				if (hasExth)
				{
					int exthStart = MobiHeaderStart + headerLength;
					if (exthStart + 12 <= record0.Length && Encoding.ASCII.GetString(record0, exthStart, 4) == "EXTH")
					{
						int exthRecordCount = (int)PalmDbReader.ReadUInt32BE(record0, exthStart + 8);
						int pos = exthStart + 12;
						for (int i = 0; i < exthRecordCount && pos + 8 <= record0.Length; i++)
						{
							int type = (int)PalmDbReader.ReadUInt32BE(record0, pos);
							int recordLength = (int)PalmDbReader.ReadUInt32BE(record0, pos + 4);
							int dataLength = recordLength - 8;
							if (dataLength < 0 || pos + recordLength > record0.Length)
							{
								break;
							}

							switch (type)
							{
								case 100: // EXTH_AUTHOR - can repeat for multiple authors.
									string a = Encoding.UTF8.GetString(record0, pos + 8, dataLength);
									author = author is null ? a : $"{author}; {a}";
									break;

								case 503: // EXTH_UPDATEDTITLE - overrides the PalmDOC full name when present.
									title = Encoding.UTF8.GetString(record0, pos + 8, dataLength);
									break;

								case 201 when dataLength == 4: // EXTH_COVEROFFSET - add to first-image-record index.
									coverIndex = (int)PalmDbReader.ReadUInt32BE(record0, pos + 8);
									break;

								case 121 when dataLength == 4: // EXTH_KF8BOUNDARY
									kf8Boundary = (int)PalmDbReader.ReadUInt32BE(record0, pos + 8);
									break;
							}

							pos += recordLength;
						}
					}
				}

				CoverRecordIndex = coverIndex.HasValue && firstImageIndex >= 0 ? firstImageIndex + coverIndex.Value : null;
			}

			Title = title;
			Author = author;
			Kf8BoundaryRecordIndex = kf8Boundary;
		}
	}
}
