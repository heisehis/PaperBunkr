using System;
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
	///
	/// When entry sizes are known up front (<paramref name="sizeOf"/>), each stream is pre-sized to
	/// exactly that many bytes - one allocation instead of the geometric doubling a growing
	/// <see cref="MemoryStream"/> does, which is Large-Object-Heap churn on 200 KB-2 MB pages
	/// (design §4.7 / §15 #4). An exactly-filled stream is then handed back by reference, no
	/// <c>ToArray</c> copy.
	/// </summary>
	public sealed class MultiExtractToStreamsCallback : IProgress, IArchiveExtractCallback
	{
		private readonly HashSet<int> wanted;
		private readonly IReadOnlyDictionary<int, long> knownSizes;
		private readonly Dictionary<int, byte[]> results = new Dictionary<int, byte[]>();
		private OutStreamWrapper current;
		private MemoryStream currentStream;
		private int currentIndex = -1;

		/// <param name="knownSizes">
		/// index -&gt; uncompressed entry size, gathered <b>before</b> the <see cref="IInArchive.Extract"/>
		/// call (7z.dll does not allow reentrant <c>GetProperty</c> from inside an extract callback).
		/// Used only to pre-size each stream; omit for the old grow-on-demand behaviour.
		/// </param>
		public MultiExtractToStreamsCallback(IEnumerable<int> indices, IReadOnlyDictionary<int, long> knownSizes = null)
		{
			wanted = new HashSet<int>(indices);
			this.knownSizes = knownSizes;
		}

		/// <summary>index -&gt; extracted bytes, captured in <see cref="SetOperationResult"/> before each stream is closed.</summary>
		public IReadOnlyDictionary<int, byte[]> GetResults() => results;

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

			long size = knownSizes != null && knownSizes.TryGetValue(index, out long s) ? s : 0;
			// Pre-size from the known uncompressed size: one allocation, no geometric-doubling LOH
			// churn on 200 KB-2 MB pages (design §4.7). Falls back to grow-on-demand when unknown.
			currentStream = size > 0 && size < int.MaxValue
				? new MemoryStream(capacity: (int)size)
				: new MemoryStream();
			currentIndex = index;
			current = new OutStreamWrapper(currentStream);
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
				// Capture the bytes while the stream is still open - OutStreamWrapper.Dispose()
				// closes the underlying MemoryStream, after which only ToArray() (not Length /
				// Capacity / GetBuffer) is legal. When the stream filled its pre-sized buffer
				// exactly, GetBuffer() is the exact array - hand it back with no extra copy.
				if (currentIndex >= 0 && currentStream != null)
				{
					results[currentIndex] = currentStream.Length == currentStream.Capacity
						? currentStream.GetBuffer()
						: currentStream.ToArray();
				}

				current.Dispose();
				current = null;
				currentStream = null;
				currentIndex = -1;
			}
		}
	}
}
