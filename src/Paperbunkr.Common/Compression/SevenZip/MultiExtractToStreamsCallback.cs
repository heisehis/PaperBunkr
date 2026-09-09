using System.Collections.Generic;
using System.IO;

namespace cYo.Common.Compression.SevenZip
{
	/// <summary>
	/// Sibling of <see cref="ExtractToStreamCallback"/> that collects <b>several</b> entries from a
	/// single <see cref="IInArchive.Extract"/> call, one <see cref="MemoryStream"/> per requested
	/// index. The point is solid archives (.7z folders, solid .rar): asking for entries
	/// <c>[mark+1 .. n]</c> in one <c>Extract</c> decompresses the solid block once and hands back
	/// every entry it passed, instead of re-decompressing from the block start for each page
	/// (docs/superpowers/specs/2026-09-08-reader-decode-cache-prefetch-pipeline-design.md §4.2).
	/// </summary>
	public sealed class MultiExtractToStreamsCallback : IProgress, IArchiveExtractCallback
	{
		private readonly HashSet<int> wanted;
		private readonly Dictionary<int, MemoryStream> results = new Dictionary<int, MemoryStream>();
		private OutStreamWrapper current;

		public MultiExtractToStreamsCallback(IEnumerable<int> indices)
		{
			wanted = new HashSet<int>(indices);
		}

		/// <summary>index -&gt; extracted bytes, populated once <see cref="IInArchive.Extract"/> returns.</summary>
		public IReadOnlyDictionary<int, byte[]> GetResults()
		{
			var final = new Dictionary<int, byte[]>(results.Count);
			foreach (var kv in results)
			{
				final[kv.Key] = kv.Value.ToArray();
			}
			return final;
		}

		public void SetTotal(long total)
		{
		}

		public void SetCompleted(ref long completeValue)
		{
		}

		public int GetStream(int index, out ISequentialOutStream outStream, AskMode askExtractMode)
		{
			outStream = null;
			if (askExtractMode != AskMode.kExtract || !wanted.Contains(index))
			{
				return 0;
			}

			var ms = new MemoryStream();
			results[index] = ms;
			current = new OutStreamWrapper(ms);
			outStream = current;
			return 0;
		}

		public void PrepareOperation(AskMode askExtractMode)
		{
		}

		public void SetOperationResult(OperationResult resultEOperationResult)
		{
			if (current != null)
			{
				current.Dispose();
				current = null;
			}
		}
	}
}
