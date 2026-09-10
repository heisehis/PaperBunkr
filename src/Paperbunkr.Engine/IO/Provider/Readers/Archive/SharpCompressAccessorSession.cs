using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace cYo.Projects.ComicRack.Engine.IO.Provider.Readers.Archive
{
	/// <summary>
	/// Keep-open <see cref="IComicAccessorSession"/> over a SharpCompress <see cref="IArchive"/> -
	/// backs <see cref="SharpCompressEngine"/> (zip/rar/7z/tar when configured) and
	/// <see cref="TarSharpZipEngine"/> (always, replacing its forward-only <c>TarInputStream</c>
	/// for the session path). The archive is opened once; entries are enumerated once and cached.
	///
	/// Non-solid formats (zip, tar, non-solid rar) give cheap any-order reads. For a <b>solid</b>
	/// archive SharpCompress's per-entry <c>OpenEntryStream()</c> throws; the session then drives a
	/// forward-only <see cref="IReader"/> from the start, caching every entry it passes (§4.2). A
	/// backward jump past the reader re-opens it from the beginning.
	/// </summary>
	public sealed class SharpCompressAccessorSession : IComicAccessorSession
	{
		private readonly Stream _stream;
		private readonly IArchive _archive;
		private readonly List<IArchiveEntry> _entries;
		private readonly Dictionary<string, int> _nameToIndex;
		private readonly bool _solid;
		private readonly Dictionary<int, byte[]> _passedByBuffer = new Dictionary<int, byte[]>();

		private IReader _solidReader;
		private int _solidMark = -1;

		private SharpCompressAccessorSession(Stream stream, IArchive archive)
		{
			_stream = stream;
			_archive = archive;
			_entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
			_nameToIndex = new Dictionary<string, int>(_entries.Count, StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < _entries.Count; i++)
			{
				string key = _entries[i].Key;
				if (!string.IsNullOrEmpty(key))
				{
					_nameToIndex[key] = i;
				}
			}
			_solid = TryIsSolid(archive);
		}

		public static SharpCompressAccessorSession TryOpen(string source)
		{
			Stream stream = null;
			IArchive archive = null;
			try
			{
				stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 17);
				archive = ArchiveFactory.OpenArchive(stream, new ReaderOptions { LeaveStreamOpen = true });
				return new SharpCompressAccessorSession(stream, archive);
			}
			catch
			{
				archive?.Dispose();
				stream?.Dispose();
				return null;
			}
		}

		private static bool TryIsSolid(IArchive archive)
		{
			try
			{
				return archive is SharpCompress.Archives.Rar.RarArchive rar && rar.IsSolid;
			}
			catch
			{
				return false;
			}
		}

		public int Count => _entries.Count;

		public byte[] ReadEntryBytes(string entryName)
		{
			if (entryName == null || !_nameToIndex.TryGetValue(entryName, out int index))
			{
				return null;
			}

			if (_passedByBuffer.TryGetValue(index, out byte[] buffered))
			{
				_passedByBuffer.Remove(index);
				return buffered;
			}

			try
			{
				return _solid ? ReadSolid(index) : ReadRandom(index);
			}
			catch
			{
				return null;
			}
		}

		private byte[] ReadRandom(int index)
		{
			using var entryStream = _entries[index].OpenEntryStream();
			return ReadAll(entryStream, _entries[index].Size);
		}

		private byte[] ReadSolid(int wantedIndex)
		{
			if (_solidReader == null || wantedIndex <= _solidMark)
			{
				_solidReader?.Dispose();
				_stream.Position = 0;
				_solidReader = ReaderFactory.OpenReader(_stream, new ReaderOptions { LeaveStreamOpen = true });
				_solidMark = -1;
			}

			while (_solidReader.MoveToNextEntry())
			{
				if (_solidReader.Entry.IsDirectory)
				{
					continue;
				}

				_solidMark++;
				string key = _solidReader.Entry.Key;
				int currentIndex = key != null && _nameToIndex.TryGetValue(key, out int idx) ? idx : _solidMark;

				using var es = _solidReader.OpenEntryStream();
				byte[] bytes = ReadAll(es, _solidReader.Entry.Size);

				if (currentIndex == wantedIndex)
				{
					return bytes;
				}
				_passedByBuffer[currentIndex] = bytes;
			}

			return null;
		}

		private static byte[] ReadAll(Stream s, long knownSize)
		{
			if (knownSize > 0 && knownSize < int.MaxValue)
			{
				var buf = new byte[knownSize];
				int off = 0;
				int r;
				while (off < buf.Length && (r = s.Read(buf, off, buf.Length - off)) > 0)
				{
					off += r;
				}
				return off == buf.Length ? buf : buf[..off];
			}

			using var ms = new MemoryStream();
			s.CopyTo(ms);
			return ms.ToArray();
		}

		public void Dispose()
		{
			_passedByBuffer.Clear();
			_solidReader?.Dispose();
			_archive?.Dispose();
			_stream?.Dispose();
		}
	}
}
