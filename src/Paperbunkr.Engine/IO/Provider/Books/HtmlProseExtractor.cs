using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace cYo.Projects.ComicRack.Engine.IO.Provider.Books
{
	/// <summary>
	/// Reduces an EPUB chapter's XHTML to plain-paragraph prose with bold/italic spans only
	/// (docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md §4) - a single
	/// regex-tokenizing pass rather than a real HTML/DOM parser, since everything else a chapter's
	/// markup might carry (images, tables, multi-column layout, embedded fonts) is deliberately
	/// dropped per the design spec. Adequate for prose novels, which are almost entirely plain
	/// paragraphs; not intended for arbitrary HTML.
	/// </summary>
	public static class HtmlProseExtractor
	{
		// Matches any tag, or a run of non-tag text - one token per iteration below.
		private static readonly Regex s_tokenPattern = new Regex(@"<[^>]*>|[^<]+", RegexOptions.Compiled);

		private static readonly Regex s_scriptOrStyleBlock =
			new Regex(@"<(script|style)\b[^>]*>.*?</\1>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

		private static readonly Regex s_bodyElement =
			new Regex(@"<body\b[^>]*>(.*)</body\s*>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

		private static readonly HashSet<string> s_paragraphBreakTags = new(StringComparer.OrdinalIgnoreCase)
		{
			"p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "li", "br", "blockquote",
		};

		public static List<BookParagraph> ExtractParagraphs(string html)
		{
			var paragraphs = new List<BookParagraph>();
			if (string.IsNullOrWhiteSpace(html))
			{
				return paragraphs;
			}

			html = s_scriptOrStyleBlock.Replace(html, string.Empty);

			// Chapter XHTML carries a <head><title>...</title></head> ahead of the real content -
			// without scoping to <body>, that title text leaks in as a phantom leading paragraph
			// the moment the first paragraph-break tag (e.g. <h1>) flushes the buffer it silently
			// accumulated in. Falls back to the whole (script/style-stripped) document for content
			// that's missing a <body> wrapper rather than producing nothing.
			var bodyMatch = s_bodyElement.Match(html);
			if (bodyMatch.Success)
			{
				html = bodyMatch.Groups[1].Value;
			}

			var text = new StringBuilder();
			var spans = new List<BookTextSpan>();
			int boldStart = -1;
			int italicStart = -1;

			void FlushParagraph()
			{
				string trimmed = text.ToString().Trim();
				if (trimmed.Length > 0)
				{
					// Spans were recorded against the untrimmed buffer - shift for any leading
					// whitespace removed by Trim() so offsets still land correctly.
					int leadingTrim = text.Length - text.ToString().TrimStart().Length;
					var adjustedSpans = new List<BookTextSpan>();
					foreach (var span in spans)
					{
						int start = Math.Max(0, span.Start - leadingTrim);
						int length = Math.Min(span.Length, trimmed.Length - start);
						if (length > 0)
						{
							adjustedSpans.Add(span with { Start = start, Length = length });
						}
					}

					paragraphs.Add(new BookParagraph { Text = trimmed, Spans = adjustedSpans });
				}

				text.Clear();
				spans.Clear();
			}

			foreach (Match token in s_tokenPattern.Matches(html))
			{
				string value = token.Value;
				if (value.Length == 0)
				{
					continue;
				}

				if (value[0] == '<')
				{
					string tagName = ParseTagName(value);
					bool isClosing = value.Length > 1 && value[1] == '/';

					if (s_paragraphBreakTags.Contains(tagName) && !isClosing)
					{
						FlushParagraph();
						continue;
					}

					if ((tagName == "b" || tagName == "strong") )
					{
						if (!isClosing)
						{
							boldStart = text.Length;
						}
						else if (boldStart >= 0)
						{
							spans.Add(new BookTextSpan(boldStart, text.Length - boldStart, true, false));
							boldStart = -1;
						}
					}
					else if (tagName == "i" || tagName == "em")
					{
						if (!isClosing)
						{
							italicStart = text.Length;
						}
						else if (italicStart >= 0)
						{
							spans.Add(new BookTextSpan(italicStart, text.Length - italicStart, false, true));
							italicStart = -1;
						}
					}

					continue;
				}

				// Plain text run - collapse internal whitespace/newlines like a browser would,
				// but preserve a single leading/trailing space so words don't run together across
				// adjacent inline tags (e.g. "very<i>important</i> word").
				string decoded = WebUtility.HtmlDecode(value);
				string collapsed = Regex.Replace(decoded, @"\s+", " ");
				text.Append(collapsed);
			}

			FlushParagraph();
			return paragraphs;
		}

		private static string ParseTagName(string tag)
		{
			int start = tag[1] == '/' ? 2 : 1;
			int end = start;
			while (end < tag.Length && !char.IsWhiteSpace(tag[end]) && tag[end] != '>' && tag[end] != '/')
			{
				end++;
			}

			return end > start ? tag.Substring(start, end - start).ToLowerInvariant() : string.Empty;
		}
	}
}
