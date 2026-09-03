using System.Text.RegularExpressions;

namespace cYo.Projects.ComicRack.Engine.IO.Provider.Books
{
	/// <summary>
	/// Injects a stable, deterministic <c>id="pb-p&lt;n&gt;"</c> onto every block-level element in a
	/// chapter's normalized HTML (docs/superpowers/specs/2026-09-02-books-reflow-reader-webview-
	/// redesign-design.md) - the shared anchor both highlights and reading-position locators use
	/// (<c>(ChapterIndex, BlockId, OffsetWithinBlock[, Length])</c>), replacing the old "global
	/// character offset into flattened plain text" model.
	///
	/// A single regex-tokenizing pass over the markup, same posture <see cref="HtmlProseExtractor"/>
	/// already uses successfully for this exact class of problem - a real DOM parser is unnecessary
	/// overhead given the input is entirely this app's own generated markup (EPUB's real content
	/// aside - see the design's own open question about EPUB specifically), not arbitrary third-party
	/// HTML with unpredictable structure.
	///
	/// Determinism (same input string in → same IDs out, every time) is the one hard requirement:
	/// stored highlights/positions only remain valid across reopens if re-running this on the same
	/// chapter HTML reproduces the same IDs in the same order.
	/// </summary>
	public static class BlockIdInjector
	{
		private static readonly Regex s_blockOpenTag = new Regex(
			@"<(p|h1|h2|h3|h4|h5|h6|li|blockquote)\b([^>]*)>",
			RegexOptions.Compiled | RegexOptions.IgnoreCase);

		private static readonly Regex s_hasIdAttribute = new Regex(
			@"\bid\s*=", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		public static string Inject(string html)
		{
			int counter = 0;

			return s_blockOpenTag.Replace(html, match =>
			{
				string tagName = match.Groups[1].Value;
				string attributes = match.Groups[2].Value;

				// An element that already carries an id (e.g. Fb2BookSource's own <binary>-resolved
				// image references never hit this path, but a future format/consumer might pre-assign
				// one) is left alone rather than double-assigned - first id wins.
				if (s_hasIdAttribute.IsMatch(attributes))
				{
					return match.Value;
				}

				counter++;
				return $"<{tagName}{attributes} id=\"pb-p{counter}\">";
			});
		}
	}
}
