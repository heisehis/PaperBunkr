using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace cYo.Projects.ComicRack.Engine.IO.Provider.Books.Mobi
{
	/// <summary>
	/// Parses a Palm OS "PDB" container's fixed 78-byte header and record-offset table into raw
	/// per-record byte slices (docs/superpowers/specs/2026-09-01-books-format-ingestion-fb2-mobi-
	/// design.md - MOBI/AZW3 foundation layer). Byte layout verified against the MobileRead Wiki's
	/// PDB article and the 766b/mobi Go reference implementation (both consulted directly, not
	/// recalled from memory, given how easy this format is to get subtly wrong): 32-byte name, then
	/// ten 2/4-byte fields up to a 2-byte record count at offset 76, then one 8-byte
	/// (4-byte offset + 1-byte attributes + 3-byte unique ID) entry per record starting at offset 78.
	/// Only the record offsets are used here - attributes/unique ID/every PDB header field besides
	/// name and record count are irrelevant to reading MOBI/AZW3 content.
	/// </summary>
	internal sealed class PalmDbReader
	{
		private const int HeaderSize = 78;
		private const int RecordCountOffset = 76;
		private const int RecordInfoSize = 8;

		public string Name { get; }

		public IReadOnlyList<byte[]> Records { get; }

		public PalmDbReader(byte[] fileBytes)
		{
			if (fileBytes.Length < HeaderSize)
			{
				throw new InvalidDataException("File is too short to contain a valid PalmDB (PDB) header.");
			}

			Name = ReadFixedString(fileBytes, 0, 32);
			int numRecords = ReadUInt16BE(fileBytes, RecordCountOffset);

			int recordTableEnd = HeaderSize + numRecords * RecordInfoSize;
			if (fileBytes.Length < recordTableEnd)
			{
				throw new InvalidDataException("PalmDB record offset table is truncated.");
			}

			var offsets = new int[numRecords];
			for (int i = 0; i < numRecords; i++)
			{
				offsets[i] = (int)ReadUInt32BE(fileBytes, HeaderSize + i * RecordInfoSize);
			}

			var records = new List<byte[]>(numRecords);
			for (int i = 0; i < numRecords; i++)
			{
				int start = offsets[i];
				int end = i + 1 < numRecords ? offsets[i + 1] : fileBytes.Length;
				if (start < 0 || end > fileBytes.Length || end < start)
				{
					throw new InvalidDataException($"PalmDB record {i} has an invalid offset range ({start}..{end}).");
				}

				var record = new byte[end - start];
				Array.Copy(fileBytes, start, record, 0, record.Length);
				records.Add(record);
			}

			Records = records;
		}

		internal static ushort ReadUInt16BE(byte[] buffer, int offset) =>
			(ushort)((buffer[offset] << 8) | buffer[offset + 1]);

		internal static uint ReadUInt32BE(byte[] buffer, int offset) =>
			((uint)buffer[offset] << 24) | ((uint)buffer[offset + 1] << 16) | ((uint)buffer[offset + 2] << 8) | buffer[offset + 3];

		private static string ReadFixedString(byte[] buffer, int offset, int length)
		{
			int end = offset;
			int max = offset + length;
			while (end < max && buffer[end] != 0)
			{
				end++;
			}

			return Encoding.ASCII.GetString(buffer, offset, end - offset);
		}
	}
}
