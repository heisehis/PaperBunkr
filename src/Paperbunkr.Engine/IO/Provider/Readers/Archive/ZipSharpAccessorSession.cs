using System;
using System.Collections.Generic;
using System.IO;
using ICSharpCode.SharpZipLib.Zip;

namespace cYo.Projects.ComicRack.Engine.IO.Provider.Readers.Archive
{
	/// <summary>
	/// Keep-open <see cref="IComicAccessorSession"/> over a SharpZipLib <see cref="ZipFile"/> on a
	/// retained <see cref="FileStream"/> - backs <see cref="ZipSharpZipEngine"/>. <see cref="ZipFile"/>
	/// parses the central directory once on construction and <c>GetInputStream</c> seeks to a local
	/// header, so per-page reads in any order are one seek + one inflate. Not thread-safe (shared
	/// base stream) - caller serialises.
	/// </summary>
	public sealed class ZipSharpAccessorSession : IComicAccessorSession
	{
		private readonly FileStream _stream;
		private readonly ZipFile _zip;

		private ZipSharpAccessorSession(FileStream stream, ZipFile zip)
		{
			_stream = stream;
			_zip = zip;
		}

		public static ZipSharpAccessorSession TryOpen(string source)
		{
			FileStream stream = null;
			try
			{
				stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, ZipSharpZipEngine.BufferSize);
				var zip = new ZipFile(stream) { IsStreamOwner = false };
				return new ZipSharpAccessorSession(stream, zip);
			}
			catch
			{
				stream?.Dispose();
				return null;
			}
		}

		public int Count
		{
			get
			{
				int n = 0;
				foreach (ZipEntry e in _zip)
				{
					if (e.IsFile)
					{
						n++;
					}
				}
				return n;
			}
		}

		public byte[] ReadEntryBytes(string entryName)
		{
			if (entryName == null)
			{
				return null;
			}

			try
			{
				ZipEntry entry = _zip.GetEntry(entryName);
				if (entry == null)
				{
					return null;
				}

				using Stream s = _zip.GetInputStream(entry);
				long size = entry.Size;
				if (size > 0 && size < int.MaxValue)
				{
					var buf = new byte[size];
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
			catch
			{
				return null;
			}
		}

		public void Dispose()
		{
			_zip?.Close();
			_stream?.Dispose();
		}
	}
}
