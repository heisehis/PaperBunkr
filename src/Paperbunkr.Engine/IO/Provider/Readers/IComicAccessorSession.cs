using System;

namespace cYo.Projects.ComicRack.Engine.IO.Provider.Readers
{
	/// <summary>
	/// A reading-session-scoped handle onto one archive: the container is opened and its index
	/// parsed <b>once</b>, then <see cref="ReadEntryBytes"/> pulls individual entries from the
	/// held-open handle. This is the counterpart to <see cref="IComicAccessor.ReadByteImage"/>'s
	/// stateless "reopen the file every call" contract, which stays as-is for scanning, metadata
	/// read/write and export - anything that isn't an open reader.
	///
	/// <b>Not thread-safe.</b> None of the underlying libraries (7z.dll COM, SharpZipLib,
	/// SharpCompress) tolerate concurrent reads off one handle; every <see cref="ReadEntryBytes"/>
	/// call must be serialised by the caller (the reader pipeline does this on a single archive
	/// thread - see docs/superpowers/specs/2026-09-08-reader-decode-cache-prefetch-pipeline-design.md
	/// §4).
	///
	/// Added net-new for the reader pipeline; ComicRack CE has no keep-open concept anywhere (its
	/// accessors all reopen per page and CE hid the cost with ImagePool + the OS file cache).
	/// </summary>
	public interface IComicAccessorSession : IDisposable
	{
		/// <summary>Entry count the session sees in the container.</summary>
		int Count { get; }

		/// <summary>
		/// The raw bytes of the entry whose in-container name is <paramref name="entryName"/> -
		/// the <see cref="ProviderImageInfo.Name"/> the owning <see cref="ImageProvider"/> already
		/// resolved for this page. Name rather than index so one contract spans engines that
		/// address entries by index (7z.dll) and by name (SharpZipLib / SharpCompress). Returns
		/// <see langword="null"/> on a read failure rather than throwing, matching
		/// <see cref="IComicAccessor.ReadByteImage"/>'s own swallow-and-return-null contract.
		///
		/// The caller is expected to request pages in roughly forward order; for solid archives an
		/// out-of-order jump backward past what has already been decompressed may re-drive the
		/// stream from an earlier point (still correct, just not free).
		/// </summary>
		byte[] ReadEntryBytes(string entryName);
	}
}
