using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace cYo.Projects.ComicRack.Engine.IO.Provider.Readers.Archive
{
	public abstract class FileBasedAccessor : IComicAccessor
	{
		private IEnumerable<byte> signature;

		protected bool HasSignature => signature != null;

		public int Format
		{
			get;
			private set;
		}

		public FileBasedAccessor(int format)
		{
			Format = format;
			signature = KnownFileFormats.GetSignature(format);
		}

		public abstract IEnumerable<ProviderImageInfo> GetEntryList(string source);

		public abstract byte[] ReadByteImage(string source, ProviderImageInfo info);

		public abstract T ReadInfo<T>(string source) where T : ComicInfo;

		public virtual bool WriteInfo(string source, ComicInfo info)
		{
			return SevenZipEngine.UpdateComicInfos(source, Format, comicInfo: info);
        }

		/// <summary>
		/// Concrete (non-DIM) so archive engines can <c>override</c> and have the override picked up
		/// through an <see cref="IComicAccessor"/> reference - a derived class adding a matching
		/// member does <b>not</b> re-map an interface slot already satisfied by a base-class default
		/// interface method. See docs/superpowers/specs/2026-09-08-reader-decode-cache-prefetch-
		/// pipeline-design.md §4.
		/// </summary>
		public virtual bool SupportsSession => false;

		public virtual IComicAccessorSession OpenSession(string source) => null;

        public virtual bool IsFormat(string source)
		{
			if (signature == null)
			{
				return true;
			}
			try
			{
				using (FileStream fileStream = File.OpenRead(source))
				{
					IEnumerable<byte> enumerable = signature;
					byte[] array = new byte[enumerable.Count()];
					fileStream.Read(array, 0, array.Length);
					return enumerable.SequenceEqual(array);
				}
			}
			catch
			{
			}
			return true;
		}

    }
}
