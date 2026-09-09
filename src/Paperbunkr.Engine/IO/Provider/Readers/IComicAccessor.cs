using System.Collections.Generic;

namespace cYo.Projects.ComicRack.Engine.IO.Provider.Readers
{
	public interface IComicAccessor
	{
		bool IsFormat(string source);

		IEnumerable<ProviderImageInfo> GetEntryList(string source);

		byte[] ReadByteImage(string source, ProviderImageInfo info);

		T ReadInfo<T>(string source) where T : ComicInfo;

		bool WriteInfo(string source, ComicInfo info);

		/// <summary>
		/// Whether this accessor can hand out a keep-open <see cref="IComicAccessorSession"/> for
		/// fast per-page reads (docs/superpowers/specs/2026-09-08-reader-decode-cache-prefetch-
		/// pipeline-design.md §4). Default <see langword="false"/> - accessors opt in.
		/// </summary>
		bool SupportsSession => false;

		/// <summary>
		/// Opens a reading-session-scoped handle onto <paramref name="source"/>, or
		/// <see langword="null"/> if unsupported / it can't be opened. Default returns
		/// <see langword="null"/>.
		/// </summary>
		IComicAccessorSession OpenSession(string source) => null;
	}
}
